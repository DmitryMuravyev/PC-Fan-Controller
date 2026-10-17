////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// fan_base.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <fan_base.h>
#include <stdlib.h>


// Implementation-level definitions
// Check the previous result and call procedure if there's no error
#define CHECK_AND_CALL(f, v)						{ if (v == HAL_OK) v = (f); }
// Call procedure and check the result
#define CALL_AND_CHECK(f)							{ HAL_StatusTypeDef status = (f); if (status != HAL_OK) return (status); }
// Call procedure and break on error (instead of goto)
#define CALL_AND_BREAK(f)							{ if ((f) != HAL_OK) break; }
// Call procedure and accumulate bad results
#define CALL_ACCUMULATE_ERROR_AND_BREAK(f, v)		{ if ((f) != HAL_OK) { v |= true; break; } }
// Call progress delegate procedure
#define CALL_PROGRESS_DELEGATE(f, t, p, c)			{ if ((f) != nullptr) f(t, p, this, c); }

// Bring the Control Value steps to a hardware supported one
#define FAN_DCDC_CONTROL_STEP						(1 << (FAN_CONTROL_BIT_WIDTH - dcdcControlBitWidth()))
#define FAN_PWM_CONTROL_STEP						(1 << (FAN_CONTROL_BIT_WIDTH - pwmControlBitWidth()))


// Initialize static variable
uint8_t Fan_base::configFlags = Fan_Config_Flag_Empty;


Fan_base::Fan_base() {
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Fan logic functions

// Error counter processing
bool Fan_base::countErrors(bool error, uint32_t * counter, uint8_t counterShift, uint32_t errorFlag, bool severe) {

	int8_t count = (*counter >> counterShift) & FAN_ERR_COUNTER_MASK;		// Get the required counter
	bool setError = false;
	bool clearError = false;

	if (error) {
		count += FAN_ERR_COUNTER_INCREMENT;
		if ((count >= FAN_ERR_COUNTER_MAX) || severe) {
			count = FAN_ERR_COUNTER_MAX;
			setError = true;
		}
		// Increment the total number of errors
		if (errorFlag != Fan_Status_Failed) state.totalErrorsCount++;
	} else {
		count -= FAN_ERR_COUNTER_DECREMENT;
		if ((count <= 0) || severe) {
			count = 0;
			clearError = true;
		}
	}

	// Update counter
	*counter &= ~((uint32_t)FAN_ERR_COUNTER_MASK << counterShift);
	*counter |= ((uint32_t)count << counterShift);

	if (setError) {
		state.status |= errorFlag;
	} else if (clearError) {
		state.status &= ~errorFlag;
	}

	return (error ? setError : clearError);
}


// Checking the possibility of using the PID at the moment
bool Fan_base::pidEnabled() {
	// if (configuration == nullptr) return (HAL_ERROR);
	// This check will be performed in the calling procedures.
	// So it's not needed here.

	uint8_t controlMode = state.controlMode;
	bool pwmControlMode = (controlMode == Fan_Control_PWM);
	if (pwmControlMode || (controlMode == Fan_Control_DCDC)) {
		// Prepare parameters for check
		uint8_t pidEnabledBitShift = pwmControlMode ? Fan_Options_PID_Enabled_PWM_Bit_Shift : Fan_Options_PID_Enabled_PWR_Bit_Shift;
		uint16_t controlRangeDetected = pwmControlMode ? Fan_Model_PWM_Range_Detected : Fan_Model_DCDC_Range_Detected;

		if (((configuration->options >> pidEnabledBitShift) & 1) &&
			((configuration->model & controlRangeDetected) != 0) &&		// Enabling the PID should not be available
																		// without a detected control range,
																		// but just in case, we'll check it too.
			(state.operationMode != Fan_Operation_Turbo)) {				// In Turbo Mode we should be at the top speed all the time.
			return (true);
		}
	}

	return (false);
}


// Check whether the PID can handle the current PPM delta.
// The main idea is something like this:
// the narrower the operating range of the fan RPM (or PPM),
// the easier it is for the PID to handle it.
bool Fan_base::pidCanHandleDelta(uint32_t currentPPM, uint32_t targetPPM, uint32_t minPPM) {
	// if (configuration == nullptr) return (HAL_ERROR);
	// This check will be performed in the calling procedures.
	// So it's not needed here.

	uint32_t ppmRange = configuration->maxPPM - minPPM;
	// In professional mode, it may also be important for us
	// to quickly reduce fan speed (to reduce power consumption
	// and increase fan life), while for home use, a gradual change
	// in noise level is more relevant (so we can slow down using the PID).
	uint32_t ppmDelta = (targetPPM > currentPPM) ? (targetPPM - currentPPM) :
			((Fan_base::configFlags & Fan_Config_Flag_Professional_Mode) != 0) ? (currentPPM - targetPPM) : 0;

	// Adjust the delta by a certain coefficient of completeness of the fan's operating range.
	ppmDelta *= ppmRange;
	ppmDelta /= configuration->maxPPM;

	return (ppmDelta < (ppmRange >> FAN_PID_OPERATING_RANGE_SHIFT));
}


// Suspend the PID and save the current timestamp for subsequent timeout checks
void Fan_base::suspendPID(bool pid) {
	if (pid) {
		pidIntegralComponent = INT32_MIN;
		state.status |= Fan_Status_PID_Suspended;
	}
	// We need to save the current time for later calculation of all possible timeouts
	serviceTick = HAL_GetTick();
}


// Reset the PID and error handling parameters
void Fan_base::resetRuntimeParameters() {
	pidPrevErr = 0;
	pidIntegralComponent = INT32_MIN;
	serviceTick = 0;
	minControlSupplementSteps = 0;
	errorPressureCounter = 0;
}


// Calculating the target PPM and Control Value
void Fan_base::calculateTargets(uint16_t control, bool isPWM, uint32_t & minPPM, uint16_t minControl, uint16_t maxControl, uint32_t & targetPPM, uint16_t & targetControl) {
	// if (configuration == nullptr) return (HAL_ERROR);
	// This check will be performed in the calling procedures.
	// So it's not needed here.

	// Take into account the Control supplement (if any)
	if (minControlSupplementSteps != 0) {
		uint16_t controlSupplement = minControlSupplementSteps * (isPWM ? FAN_PWM_CONTROL_STEP : FAN_DCDC_CONTROL_STEP);

		// TODO Calculate minPPM based on approximation polynomial

		minPPM = Fan_base::mapUInt(minControl + controlSupplement, minControl, maxControl, minPPM, configuration->maxPPM);
		minControl += controlSupplement;
	}

	// Calculate targets
	targetPPM = Fan_base::mapUInt(control, 0, FAN_CONTROL_TOP, minPPM, configuration->maxPPM);

	// If the fan has a significant speed increase between 99% and 100% PWM,
	// then we need to correct this. In addition, in this case, the PID will
	// be turned off at 100% PWM, so that self-oscillation does not occur.

	// TODO Calculate targetControl based on reversed approximation polynomial

	if (isPWM && ((configuration->model & Fan_Model_PWM_Excessive_Full_Speed) != 0)) {
		// Let's analyze which point the Target PPM is closer to:
		// the penultimate one, the top one, or it is in the lower range of PPM.
		if (targetPPM > configuration->pwmPenultimatePPM) {
			if (targetPPM <= ((configuration->pwmPenultimatePPM + configuration->maxPPM) >> 1)) {
				targetPPM = configuration->pwmPenultimatePPM;
				targetControl = FAN_CONTROL_TOP - FAN_SCAN_PWM_EXCESSIVE_FS_STEP;
			} else {
				targetPPM = configuration->maxPPM;
				targetControl = FAN_CONTROL_TOP;
			}
		} else {
			// Since in this case we are starting from RPM and
			// it is the linearity of revolutions that we want to ensure,
			// we will recalculate the target Control Value based on RPM (PPM).
			// But it is still better to do it via approximation polynomial.
			targetControl = (uint16_t)Fan_base::mapUInt(targetPPM, minPPM, configuration->pwmPenultimatePPM, minControl, FAN_CONTROL_TOP - FAN_SCAN_PWM_EXCESSIVE_FS_STEP);
		}

	} else {
		targetControl = (uint16_t)Fan_base::mapUInt(control, 0, FAN_CONTROL_TOP, minControl, maxControl);
	}
}


// Check whether the available equipment allows the specified Control Mode.
bool Fan_base::controlModeAvailable(uint8_t mode, Fan_Config * config) {
	// if (config == nullptr) return (HAL_ERROR);
	// This check will be performed in the calling procedures.
	// So it's not needed here.

	switch (mode & Fan_Control_Mask) {

		case Fan_Control_PWM:

			// Check if PWM control available
			if (((config->hardware & Fan_Hardware_PWM_Available) != 0) &&
					(config->model & Fan_Model_PWM) != 0) return (true);
			break;

		case Fan_Control_DCDC:

			// Check if DC-DC available and the fan is connected
			if (((config->hardware & Fan_Hardware_DCDC_Available) != 0) &&
					((config->model & Fan_Model_Connected) != 0)) return (true);
			break;

		case Fan_Control_MOSFET:

			// Check if MOSFET on/off control available and the fan is connected
			if (((config->hardware & Fan_Hardware_MOSFET_Available) != 0) &&
					((config->model & Fan_Model_Connected) != 0)) return (true);
			break;

		case Fan_Control_Disabled:

			return (true);

		default:
			break;
	}

	return (false);
}


// Determine the best Control Mode, taking into account
// the available equipment and accumulated errors.
uint8_t Fan_base::getBestControlMode(Fan_Config * config) {
	// if (config == nullptr) return (HAL_ERROR);
	// This check will be performed in the calling procedures.
	// So it's not needed here.

	// Let's analyze the available resources and draw conclusions
	// about the best Control Mode.
	if (config != nullptr) {

		bool pwmAvailable = (config->hardware & Fan_Hardware_PWM_Available) != 0;
		bool dcdcAvailable = (config->hardware & Fan_Hardware_DCDC_Available) != 0;
		bool mosfetAvailable = (config->hardware & Fan_Hardware_MOSFET_Available) != 0;

		bool fanModelConnected = (config->model & Fan_Model_Connected) != 0;
		bool fanModelPWM = (config->model & Fan_Model_PWM) != 0;
		bool fanModelPWMRangeDetected = (config->model & Fan_Model_PWM_Range_Detected) != 0;
		bool fanModelDCDCRangeDetected = (config->model & Fan_Model_DCDC_Range_Detected) != 0;

		bool pwmOK = (state.status & Fan_Status_PWM_HW_Error) == 0;
		//bool tachOK = (state.status & Fan_Status_TACH_HW_Error) == 0;
		bool dcdcOK = (state.status & Fan_Status_DCDC_HW_Error) == 0;
		//bool vmeterOK = (state.status & Fan_Status_Voltmeter_HW_Error) == 0;
		bool mosfetOK = (state.status & Fan_Status_MOSFET_HW_Error) == 0;

		// We will count the points for each of the possible Modes
		uint8_t pwm = 0;
		uint8_t dcdc = 0;
		uint8_t mosfet = 0;

		if (pwmAvailable && fanModelPWM && pwmOK) {
			pwm++;
			if (fanModelPWMRangeDetected) pwm++;
		}

		if (dcdcAvailable && fanModelConnected && dcdcOK) {
			dcdc++;
			if (fanModelDCDCRangeDetected) dcdc++;
		}

		if (mosfetAvailable && fanModelConnected && mosfetOK) mosfet++;

		// The most preferred Control Mode is PWM,
		if ((pwm > 0) && (pwm >= dcdc)) return (Fan_Control_PWM); else
			// followed by DC-DC...
			if ((dcdc > 0) && (dcdc >= mosfet)) return (Fan_Control_DCDC); else
				// ... and in last place MOSFET ON/OFF Mode.
				if (mosfet > 0) return (Fan_Control_MOSFET);
	}

	return (Fan_Control_Disabled);
}


// Get the spin-up Control Value from the parameters defined at the fan scanning stage.
uint16_t Fan_base::getSpinUpControlFromOption(uint16_t option, uint8_t shift) {
	if (shift) {
		option >>= shift;
		option &= Fan_Options_Spin_Mask;
	}

	uint32_t spinUpControl = FAN_CONTROL_QUARTER_RANGE * option;
	if (spinUpControl > FAN_CONTROL_TOP) spinUpControl = FAN_CONTROL_TOP;

	return ((uint16_t)spinUpControl);
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Hardware management functions

// Set a temporary Control Mode for performing all kinds of service operations
HAL_StatusTypeDef Fan_base::setServiceMode(uint8_t mode) {
	if (mode == serviceMode) return (HAL_OK);

	uint8_t realMode;
	uint16_t realControl;

	// Return to normal Control Mode
	if (mode == Fan_Service_Mode_None) {
		realMode = state.controlMode;

		// In service mode, we could do all sorts of strange things for a long time,
		// so it would be better if we reset some previous values.
		lastVoltage = FAN_INDEFINITE_VOLTAGE;
		lastPPM = FAN_INDEFINITE_PPM;
		lastUpdateTick = 0;
	} else {
		realMode = mode;
	}

	// Get the actual Control Value from the hardware
	CALL_AND_CHECK(getRealControlForMode(realMode, realControl));

	// These values are used by doIncrements() in the ADC interrupt,
	// so they must be changed simultaneously. The interrupt state
	// is restored, since we may be called with interrupts disabled (kernelPanic()).
	uint32_t primask = __get_PRIMASK();
	__disable_irq();
	targetControl = currentControl = realControl;
	serviceMode = mode;
	__set_PRIMASK(primask);

	return (HAL_OK);
}


// The main and only point where we directly control the equipment.
// The rest of the control is performed indirectly,
// through the procedures for smoothly changing the Control Value.
HAL_StatusTypeDef Fan_base::driverSetControl(uint16_t control, bool highPriority) {
	uint8_t controlMode = (serviceMode == Fan_Service_Mode_None) ? state.controlMode : serviceMode;
	switch (controlMode) {
		case Fan_Control_PWM:
			return (setPWM(control, highPriority));

		case Fan_Control_DCDC:
			return (setDCDC(control, highPriority));

		case Fan_Control_MOSFET:
			return (setMOSFET(control, highPriority));

		default:
		// Do nothing
			break;
	}
	return (HAL_OK);
}


// Get the actual Control Value from the hardware
HAL_StatusTypeDef Fan_base::getRealControlForMode(uint8_t mode, uint16_t & control) {
	switch (mode) {
		case Fan_Control_PWM:
			CALL_AND_CHECK(getPWM(control));
			break;

		case Fan_Control_DCDC:
			CALL_AND_CHECK(getDCDC(control));
			break;

		case Fan_Control_MOSFET:
			CALL_AND_CHECK(getMOSFET(control));
			break;

		case Fan_Control_Disabled:
		default:
			control = currentControl;
			break;
	}

	return (HAL_OK);
}


// Instant or delayed (smooth) setting of the Control Value in the hardware
HAL_StatusTypeDef Fan_base::setControlTarget(uint16_t control, bool immediately, bool wait) {
	if ((control != targetControl) &&
			((state.controlMode != Fan_Control_Disabled) || (serviceMode != Fan_Service_Mode_None))) {

		// If the configuration is not available,
		// then we will take the default coefficient of smooth change.
		uint16_t rocValue = (configuration == nullptr) ? Fan_Options_ROC_Default :
				(configuration->options >> Fan_Options_ROC_Bit_Shift) & Fan_Options_ROC_Mask;
		targetControl = control;

		// If we need to set the Control value immediately,
		// then turn to the hardware right here.
		if (immediately || (rocValue == Fan_Options_ROC_Disabled) || (state.controlMode == Fan_Control_MOSFET)) {
			currentControl = control;
			CALL_AND_CHECK(driverSetControl(control));
		}

		// If it is necessary to wait, then we will wait until the algorithm
		// of smooth change brings the Control Value to the target, or
		// until the time required for this operation expires (with a double margin).
		if (wait) {
			int32_t delta = targetControl;
			delta -= currentControl;
			uint32_t deltaUpdateTimeout = ((abs(delta) / (1 << rocValue) + 1) * FAN_ROC_UPDATE_TIME_INTERVAL) << 1;
			uint32_t tickStart = HAL_GetTick();
			bool canWaitMore;

			while (((canWaitMore = ((HAL_GetTick() - tickStart) < deltaUpdateTimeout))) &&
					(currentControl != targetControl)) {};

			if (!canWaitMore) return (HAL_TIMEOUT);
		}
	}

	return (HAL_OK);
}


// Recalculation of Control and Target Values based on Control and Operation Modes
HAL_StatusTypeDef Fan_base::updateControlValues() {
	// if (configuration == nullptr) return (HAL_ERROR);
	// This check will be performed in the calling procedures.
	// So it's not needed here.

	// Calculate scaled Control value according to the Operation mode
	switch (state.operationMode) {

		default:
		case Fan_Operation_Normal:

			// A Control Value should be scaled to the current fan Control basis.
			//
			// FAN_CONTROL_FULL_RANGE = 0x10000
			// FAN_CONTROL_HALF_RANGE = 0x8000
			//
			// Example 1:
			// baseControl = 0x8000
			// startPoint = 0; range = 0x10000 = 0xFFFF
			//
			// Example 2:
			// baseControl = 0x7FFF
			// startPoint = 0; range = 0xFFFE = 0xFFFD
			//
			// Example 3:
			// baseControl = 0x8001
			// startPoint = 2; range = 0xFFFE = 0xFFFD
			//
			// Example 4 (We should avoid such cases, but nevertheless):
			// baseControl = 0xFFFF
			// startPoint = 0xFFFE; range = 0x2 = 0x1
			//
			// Example 5 (We should avoid such cases too):
			// baseControl = 0x0000
			// startPoint = 0; range = 0 = 0
			//
			// Example 6 (baseControl = 25% = 0x4000)
			// startPoint = 0; range = 0x8000
			// Control =  0x4000 (25%); correctedControl = 0x2000 (12.5%)
			// Control =  0x8000 (50%); correctedControl = 0x4000 (25%)
			// Control =  0xC000 (75%); correctedControl = 0x6000 (37.5%)

			uint32_t startPoint;
			uint32_t endPoint;
			if (FAN_CONTROL_HALF_RANGE < state.controlBasis) {
				startPoint = (state.controlBasis - FAN_CONTROL_HALF_RANGE) << 1;
				endPoint = FAN_CONTROL_FULL_RANGE;
			} else {
				startPoint = 0;
				endPoint = FAN_CONTROL_FULL_RANGE - ((FAN_CONTROL_HALF_RANGE - state.controlBasis) << 1);
			}

			if (endPoint) endPoint--;
			state.scaledControl = (uint16_t)Fan_base::mapUInt(state.controlValue, 0, FAN_CONTROL_TOP, startPoint, endPoint);

			// Here we must take into account the supplement
			// that compensates for failed fans.
			if ((FAN_CONTROL_TOP - state.scaledControl) < extraSupplement) {
				state.scaledControl = FAN_CONTROL_TOP;
			} else {
				state.scaledControl += extraSupplement;
			}

			break;

		case Fan_Operation_Manual:
			state.scaledControl = state.controlValue;
			break;

		case Fan_Operation_Silent:
			state.scaledControl = 0;
			break;

		case Fan_Operation_Turbo:
			state.scaledControl = FAN_CONTROL_TOP;
			break;
	}

	// We don't need calculations of control values if the fan isn't
	// running or in the process of starting/stopping.
	if ((serviceMode != Fan_Service_Mode_None) ||
		((state.status & Fan_Status_Running) == 0) ||
		((state.status & Fan_Status_Spinning_Up) != 0)) {
		return (HAL_OK);
	}

	uint32_t minPPM = 0;
	uint16_t correctedControl = 0;			// Control Value adjusted relative to the current Control Mode (PWM/Power/On/Off).

	// Adjust the scaled Control Value according to the Control Mode
	switch (state.controlMode) {
		case Fan_Control_PWM:

			minPPM = configuration->pwmControlledMinPPM;
			calculateTargets(state.scaledControl, true, minPPM, configuration->minPWMControl, FAN_CONTROL_TOP, targetPPM, correctedControl);

			break;

		case Fan_Control_DCDC:

			minPPM = configuration->pwrControlledMinPPM;
			calculateTargets(state.scaledControl, false, minPPM, configuration->minPWRControl, configuration->maxVoltageControl, targetPPM, correctedControl);

			break;

		case Fan_Control_MOSFET:
			if (state.scaledControl > (FAN_CONTROL_HALF_RANGE + FAN_MOSFET_MODE_HYSTERESIS)) {
				correctedControl = FAN_CONTROL_TOP;
			} else if (state.scaledControl < (FAN_CONTROL_HALF_RANGE - FAN_MOSFET_MODE_HYSTERESIS)) {
				correctedControl = 0;
			} else {
				// Within the hysteresis band, keep the current state
				correctedControl = (targetControl >= FAN_CONTROL_HALF_RANGE) ? FAN_CONTROL_TOP : 0;
			}

			if (correctedControl == FAN_CONTROL_TOP) targetPPM = configuration->maxPPM; else
				targetPPM = 0;
			break;

		default:
			// Do nothing
			break;
	}

	bool pid = pidEnabled();
	bool significantChange = pid && !pidCanHandleDelta(state.currentPPM, targetPPM, minPPM);
	// Significant changes require a fast fan response,
	// so in such cases we bypass the PID.
	// Here we also taking into account the need to immediately apply
	// the control value in the case of the MOSFET Control Mode.
	if (!pid || significantChange) {
		CALL_AND_CHECK(setControlTarget(correctedControl, false, false));
		suspendPID(pid);
	} else {
		// It is necessary that the PID has worked
		// at least once before checking the speed.
		state.status |= Fan_Status_PID_Action_Required;
	}

	return (HAL_OK);

/*
	// As one of the options, we can calculate the expected time for the fan
	// to reach the target RPM. And until this time expires, do not check
	// the correctness of the PPM. This option turned out to be unreliable to me,
	// but I'll leave my drafts on this topic here, in case they ever come in handy.

	// There are many possible options for spinning up or decelerating the fan.
	// To understand them, it is best to draw diagrams and place the points
	// in accordance with the cases listed below (conditions are shown on the left,
	// and the waiting time for PPM stabilization is on the right):

	// spinDuration - the fan spin-up time from minimum to maximum RPM.
	// stopDuration - the same for decelerating from max to min.

//  1) accelerationDirection = 0 && targetPPM > currentPPM											= (targetPPM - currentPPM) * spinDuration
//  2) accelerationDirection = 0 && targetPPM < currentPPM											= (currentPPM - targetPPM) * stopDuration

//  3) accelerationDirection > 0 && targetPPM > targetPPM && currentPPM < targetPPM					+ (targetPPM - targetPPM) * spinDuration
//  4) accelerationDirection > 0 && targetPPM > targetPPM && currentPPM > targetPPM					= (currentPPM - targetPPM) * stopDuration
//  5) accelerationDirection > 0 && targetPPM > targetPPM && targetPPM > currentPPM > targetPPM		= (targetPPM - currentPPM) * spinDuration

//  6) accelerationDirection > 0 && targetPPM < targetPPM && currentPPM > targetPPM					= (currentPPM - targetPPM) * stopDuration
//  7) accelerationDirection > 0 && targetPPM < targetPPM && currentPPM < targetPPM					- (targetPPM - targetPPM) * spinDuration
//  8) accelerationDirection > 0 && targetPPM < targetPPM && targetPPM < currentPPM < targetPPM		= (currentPPM - targetPPM) * stopDuration

//  9) accelerationDirection < 0 && targetPPM > targetPPM && currentPPM < targetPPM					= (targetPPM - currentPPM) * spinDuration
// 10) accelerationDirection < 0 && targetPPM > targetPPM && currentPPM > targetPPM					- (targetPPM - targetPPM) * stopDuration
// 11) accelerationDirection < 0 && targetPPM > targetPPM && targetPPM > currentPPM > targetPPM		= (targetPPM - currentPPM) * spinDuration

// 12) accelerationDirection < 0 && targetPPM < targetPPM && currentPPM > targetPPM					+ (targetPPM - targetPPM) * stopDuration
// 13) accelerationDirection < 0 && targetPPM < targetPPM && currentPPM < targetPPM					= (targetPPM - currentPPM) * spinDuration
// 14) accelerationDirection < 0 && targetPPM < targetPPM && targetPPM < currentPPM < targetPPM		= (currentPPM - targetPPM) * stopDuration

		int32_t newTimeToStabilize = state.timeToStabilize;
		if (state.accelerationDirection == 0) {
			if (targetPPM > currentPPM) {
				// 1
				newTimeToStabilize = (targetPPM - currentPPM) * timeToSpin / configuration.maxPPM;
			} else {
				// 2
				newTimeToStabilize = (currentPPM - targetPPM) * timeToStop / configuration.maxPPM;
			}
		} else
		if (state.accelerationDirection > 0) {
			if (targetPPM > targetPPM) {
				if (currentPPM < targetPPM) {
					// 3
					newTimeToStabilize += (currentPPM - targetPPM) * timeToSpin / configuration.maxPPM;
				} else
				if (currentPPM > targetPPM) {
					// 4
					newTimeToStabilize = (currentPPM - targetPPM) * timeToStop / configuration.maxPPM;
				} else {
					// 5
					newTimeToStabilize = (targetPPM - currentPPM) * timeToSpin / configuration.maxPPM;
				}
			} else {
				if (currentPPM > targetPPM) {
					// 6
					newTimeToStabilize = (currentPPM - targetPPM) * timeToStop / configuration.maxPPM;
				} else
				if (currentPPM < targetPPM) {
					// 7
					newTimeToStabilize -= (targetPPM - targetPPM) * timeToSpin / configuration.maxPPM;
				} else {
					// 8
					newTimeToStabilize = (currentPPM - targetPPM) * timeToStop / configuration.maxPPM;
				}
			}
		} else
		if (state.accelerationDirection < 0) {
			if (targetPPM > targetPPM) {
				if (currentPPM < targetPPM) {
					// 9
					newTimeToStabilize = (targetPPM - currentPPM) * timeToSpin / configuration.maxPPM;
				} else
				if (currentPPM > targetPPM) {
					// 10
					newTimeToStabilize -= (targetPPM - targetPPM) * timeToStop / configuration.maxPPM;
				} else {
					// 11
					newTimeToStabilize = (targetPPM - currentPPM) * timeToSpin / configuration.maxPPM;
				}
			} else
			if (targetPPM < targetPPM) {
				if (currentPPM > targetPPM) {
					// 12
					newTimeToStabilize += (targetPPM - targetPPM) * timeToStop / configuration.maxPPM;
				} else
				if (currentPPM < targetPPM) {
					// 13
					newTimeToStabilize = (targetPPM - currentPPM) * timeToSpin / configuration.maxPPM;
				} else {
					// 14
					newTimeToStabilize = (currentPPM - targetPPM) * timeToStop / configuration.maxPPM;
				}
			}
		}

		if (newTimeToStabilize < 0) newTimeToStabilize = 0;
		state.timeToStabilize = 0;
*/
}


// Make sure that the PWM and DC-DC are set to 0, and then turn on the MOSFET
HAL_StatusTypeDef Fan_base::safelyTurnOnMOSFET(bool dcdcHardwareAvailable, bool pwmHardwareAvailable) {
	// First, DC-DC...
	if (dcdcHardwareAvailable) {
		CALL_AND_CHECK(setServiceMode(Fan_Service_Mode_DCDC));
		CALL_AND_CHECK(setControlTarget(0, true, false));
	}
	// ...and then PWM.
	if (pwmHardwareAvailable) {
		CALL_AND_CHECK(setServiceMode(Fan_Service_Mode_PWM));
		CALL_AND_CHECK(setControlTarget(0, true, false));
	}
	// Turn the MOSFET on
	// Let's do it through the target Control management procedures.
	CALL_AND_CHECK(setServiceMode(Fan_Service_Mode_MOSFET));
	CALL_AND_CHECK(setControlTarget(FAN_CONTROL_TOP, true, false));

	return (HAL_OK);
}


// Turn on all the hardware to the maximum
HAL_StatusTypeDef Fan_base::everythingOn100percent(bool mosfetHardwareAvailable, bool dcdcHardwareAvailable, bool pwmHardwareAvailable, bool panic) {
	uint16_t tmpVar;

	// If the MOSFET is turned off, it will be good if we set the DC-DC and PWM to 0
	// so that there are no large transients after turning on the MOSFET.
	if (mosfetHardwareAvailable) {
		CALL_AND_CHECK(getMOSFET(tmpVar));
		if (tmpVar == 0) CALL_AND_CHECK(safelyTurnOnMOSFET(dcdcHardwareAvailable, pwmHardwareAvailable));
	}

	// Now turn the DC-DC and PWM to maximum
	// First the DC-DC...
	if (dcdcHardwareAvailable) {
		CALL_AND_CHECK(setServiceMode(Fan_Service_Mode_DCDC));
		CALL_AND_CHECK(setControlTarget(FAN_CONTROL_TOP, panic, !panic));
	}
	// ...and then PWM.
	if (pwmHardwareAvailable) {
		CALL_AND_CHECK(setServiceMode(Fan_Service_Mode_PWM));
		CALL_AND_CHECK(setControlTarget(FAN_CONTROL_TOP, panic, !panic));
	}

	return (HAL_OK);
}


// Spin-up the fan to the specified Control Value
// and wait until the PPM is consistently greater than 0.
HAL_StatusTypeDef Fan_base::spinUpNow(uint16_t spinUpControl, uint32_t * outPPM) {
	uint32_t ppm;
	uint8_t startAttempts = 0;
	do {
		// Set Control value...
		if ((startAttempts != 0) && (spinUpControl < FAN_CONTROL_TOP)) {
			if ((FAN_CONTROL_TOP - spinUpControl) < FAN_CONTROL_HALF_RANGE) {
				spinUpControl += FAN_CONTROL_HALF_RANGE;
			} else {
				spinUpControl = FAN_CONTROL_TOP;
			}
		}
		CALL_AND_CHECK(setControlTarget(spinUpControl, false, false));

		bool targetReached = false;
		uint8_t nonZeroPPMCount = 0;
		uint32_t tickStart;

		// ...and wait for signs of life from the tacho sensor
		do {
			if (!targetReached) {
				tickStart = HAL_GetTick();
				targetReached = (currentControl == targetControl);
			} else {
				if ((HAL_GetTick() - tickStart) >= FAN_SCAN_SPIN_UP_TIMEOUT) return (HAL_TIMEOUT);
			}

			HAL_Delay(pulseMeasurementDelay());
			CALL_AND_CHECK(getPPM(ppm));
			if (ppm != 0) {
				nonZeroPPMCount++;
			} else {
				nonZeroPPMCount = 0;
			}
		} while (nonZeroPPMCount < FAN_SCAN_PPM_UNIFORM_CHANGES);

		// If everything worked out, then let's stop moving to the target point.
		if (!targetReached) CALL_AND_CHECK(setControlTarget(currentControl, false, false));

		// Just in case, let's make sure everything is OK,
		// and at the same time prepare the driver for accurate RPM measurement.
		CALL_AND_CHECK(getCrediblePPM(ppm));

	// The built-in fan controller can sometimes start it
	// only on the 2nd or 3rd attempt (at the same control signal level).
	} while ((ppm == 0) && (++startAttempts < FAN_SCAN_PPM_MAX_START_ATTEMPTS));

	if (outPPM) *outPPM = ppm;

	return (HAL_OK);
}


// We have to monitor the PPM during the warming-up
HAL_StatusTypeDef Fan_base::warmUpAndGetPPM(uint32_t warmUpDuration, uint32_t & ppm) {
	uint32_t errorsCount = 0;
	uint8_t zeroPPMCount = 0;
	uint32_t tickStart = HAL_GetTick();
	while((HAL_GetTick() - tickStart) < warmUpDuration) {
		if (getPPM(ppm) != HAL_OK) {
			errorsCount++;
		} else {
			if (ppm == 0) {
				if (++zeroPPMCount > FAN_SCAN_PPM_UNIFORM_CHANGES) break;
			} else {
				zeroPPMCount = 0;
			}
		}
		HAL_Delay(pulseMeasurementDelay());
	}
	// Return an error if the error occurred in more than half of the calls
	return (errorsCount > (warmUpDuration / (pulseMeasurementDelay() << 1)) ? HAL_ERROR : HAL_OK);
}


// Sets the voltage on DC-DC and either waits for it to stabilize,
// or checks whether the DC-DC output voltage changes along with the Control Value change.
HAL_StatusTypeDef Fan_base::refinePWRControlPoint(uint16_t * startPoint, int8_t steps) {

	int32_t currentPoint = startPoint != nullptr ? *startPoint : 0;
	uint16_t previousVoltage;
	uint16_t voltageStep = dcdcControlVoltageStep();
	int32_t stepThreshold = (steps == 0) ? (int32_t)(voltageStep >> 1) : (int32_t)voltageStep * steps / 2;
	uint8_t uniformChangeCount = 0;

	uint32_t tickStart = HAL_GetTick();
	bool prevPointAvailable = false;
	bool pointFound = false;
	CALL_AND_CHECK(setServiceMode(Fan_Service_Mode_DCDC));

	while ( ((steps == 0) && ((HAL_GetTick() - tickStart) < FAN_SCAN_VOLTAGE_TIMEOUT)) ||
			((steps > 0) && (currentPoint <= FAN_CONTROL_TOP)) ||
			((steps < 0) && (currentPoint >= 0)) ) {

		// We may want to refine even when we don't have a DC-DC
		if (((steps != 0) || !prevPointAvailable) && (startPoint != nullptr)) {
			CALL_AND_CHECK(setControlTarget((uint16_t)currentPoint, false, true));
		}

		// During the reverse run, we have to increase the pause
		// due to the output and filter capacitors.
		// It would be possible to calculate or measure the exact time
		// it takes for the DC-DC to change the voltage by 1 step.
		// But we'll just take a constant with a good margin.
		HAL_Delay((steps < 0) ? FAN_SCAN_VOLTAGE_TIME_CONSTANT << 1 : FAN_SCAN_VOLTAGE_TIME_CONSTANT);

		uint16_t voltageADC;
		CALL_AND_CHECK(getCredibleVoltageADC(voltageADC));

		if (prevPointAvailable) {
			// Here, all calculations have already been shifted to ADC_CHANNEL_FRACTION_BITS,
			// so we don't need to do anything to increase accuracy.
			int32_t delta = (int32_t)voltageADC - previousVoltage;
			bool uniformChange = false;

			if (steps == 0) {
				// Here we're waiting for the value to stabilize.
				uniformChange = abs(delta) < stepThreshold;
			} else
			if (steps > 0) {
				// Moving up from point to point and waiting for the value to rise.
				uniformChange = delta > stepThreshold;
			} else { // (steps < 0) {
				// Moving down from point to point and waiting for the value to fall.
				uniformChange = delta < stepThreshold;
			}

			if (uniformChange) {
				if (++uniformChangeCount >= FAN_SCAN_VOLTAGE_UNIFORM_CHANGES) {
					if (steps != 0) {
						// We need to make sure that we didn't miss the desired point if we were moving in big steps.
						// So add (steps / 2).
						currentPoint -= FAN_DCDC_CONTROL_STEP * (uniformChangeCount * steps + (steps / 2));
					}
					pointFound = true;
					break;
				}
			} else {
				uniformChangeCount = 0;
			}

		} else {
			prevPointAvailable = true;
		}

		previousVoltage = voltageADC;
		currentPoint += steps * FAN_DCDC_CONTROL_STEP;
	}

	// Here we have to check if the result goes beyond the starting point.
	if (pointFound) {
		if ( (startPoint != nullptr) &&
			 ( ((steps > 0) && (currentPoint > (int32_t)(uint32_t)*startPoint)) ||
			   ((steps < 0) && (currentPoint < (int32_t)(uint32_t)*startPoint)) ) ) {
			*startPoint = (uint16_t)currentPoint;
		}
		return (HAL_OK);
	}

	if (steps == 0) {
		return (HAL_TIMEOUT);
	}
	return (HAL_ERROR);
}


// Waits for the fan speed to stabilize.
// In the next version, it makes sense to track speed stabilization
// by approximating with a first-degree polynomial (straight line)
// and estimating the inclination angle of this line.
HAL_StatusTypeDef Fan_base::waitRotationToStabilize(uint32_t & ppm, uint32_t topPPM, int8_t expectedChange, int8_t * changeDirection, uint32_t timeout) {
	uint32_t tickStart = HAL_GetTick();
	uint32_t maxDelta;
	uint32_t maxPPM;

	int16_t deltaHistory[FAN_SCAN_PPM_POINTS];
	uint8_t deltaHistoryIndex = 0;
	int32_t deltaSum = 0;
	int8_t ppmChangeDirection = 0;
	int32_t previousPPM = -1;
	int32_t ppmEMA = -1;

	uint8_t pointsCount = 0;
	uint8_t stablePointsCount = 0;
	uint8_t consecutiveZerosCount = 0;
	uint8_t directionChangesCount = 0;
	uint8_t startAttempts = 0;
	uint8_t wrongPPMCount = 0;
	uint8_t wrongDeltaCount = 0;
	uint32_t zeroesMap = 0;

	if (changeDirection) *changeDirection = SCHAR_MIN;

	// Prepare custom constants
	if (topPPM == 0) {
		maxDelta = FAN_INDEFINITE_PPM;
		maxPPM = FAN_INDEFINITE_PPM;
	} else {
		// If the maximum rotation speed has already been detected,
		// then we can discard erroneous overestimated readings.
		maxDelta = topPPM >> FAN_SCAN_PPM_MAX_DELTA_SHIFT;
		maxPPM = topPPM << FAN_SCAN_PPM_MAX_VALUE_SHIFT;
	}

	// Clean the history of delta values before we start
	memset(deltaHistory, 0, sizeof(deltaHistory));

	// Check timeout
	while ((HAL_GetTick() - tickStart) < timeout) {

		uint32_t currentPPM;
		CALL_AND_CHECK(getCrediblePPM(currentPPM));

		do {
			if (currentPPM >= maxPPM) {
				// Sometimes, when the supply voltage is low, the fans can emit noise
				// into the tachometer line. In this case, we cannot rely on such readings
				// and cannot assume that the fan is working stably.
				// We also need to cut off infinite values outside the measurement range.
				wrongPPMCount++;
				if (wrongPPMCount >= FAN_SCAN_PPM_MAX_WRONG_PPM_COUNT) {
					return (HAL_TIMEOUT);
				} else {
					break;
				}
			}

			// Count zeros and detect if we're stopped
			bool isZero = (currentPPM == 0);
			if (isZero) {
				consecutiveZerosCount++;
				if (consecutiveZerosCount > FAN_SCAN_PPM_UNIFORM_CHANGES) {
					ppm = 0;
					if (changeDirection) *changeDirection = 0;
					return (HAL_OK);
				}
			} else {
				consecutiveZerosCount = 0;
			}

			// Store zeroes positions, so that we can understand later that the rotation is unstable.
			zeroesMap <<= 1;
			zeroesMap |= isZero;
			zeroesMap &= ((1 << FAN_SCAN_PPM_POINTS) - 1);


			if (previousPPM >= 0) {
				// Sometimes the fans try to start, but they can't overcome the inertia.
				// To track such cases, we will limit the maximum number of launch attempts
				// (we can also count the number of starts/stops, this will double the
				// resulting value, but there is no special point in this).
				//if ((previousPPM == 0) != (currentPPM == 0)) {
				if ((previousPPM == 0) && (currentPPM > 0)) {
					startAttempts++;
					if (startAttempts > FAN_SCAN_PPM_MAX_START_ATTEMPTS) return (HAL_TIMEOUT);
				}

				// Calculate delta
				int32_t delta = (int32_t)currentPPM - previousPPM;

				// Cut off deceleration or acceleration so that the detection works faster
				if ((consecutiveZerosCount <= 1) && (expectedChange != Fan_Speed_No_Change)) {
					if ((delta * expectedChange) > 0) {
						break;
					} else {
						expectedChange = Fan_Speed_No_Change;
					}
				}

				// Trying to take into account too strong rotation speed changes
				if ((uint32_t)abs(delta) > maxDelta) {
					wrongDeltaCount++;
					if (wrongDeltaCount > FAN_SCAN_PPM_MAX_WRONG_DELTA_COUNT) {
						return (HAL_TIMEOUT);
					} else {
						break;
					}
				}

				if (pointsCount < FAN_SCAN_PPM_POINTS) pointsCount++;

				// Calculate the sum of the deltas
				if (++deltaHistoryIndex >= FAN_SCAN_PPM_POINTS) deltaHistoryIndex = 0;
				deltaSum -= deltaHistory[deltaHistoryIndex];
				if (delta > SHRT_MAX) delta = SHRT_MAX; else
					if (delta < SHRT_MIN) delta = SHRT_MIN;
				deltaSum += delta;
				deltaHistory[deltaHistoryIndex] = delta;

				// Calculate PPM Exponential Moving Average
				if (ppmEMA < 0) {
					ppmEMA = currentPPM << FAN_PPM_FRACTION_BITS;
				} else {
					ppmEMA += ((((int32_t)currentPPM << FAN_PPM_FRACTION_BITS) - ppmEMA) >> 2);
				}

				// We can use division to calculate threshold...
				//int32_t deltaThreshold = (ppmEMA / FAN_SCAN_PPM_DELTA_THRESHOLD_FACTOR) >> FAN_PPM_FRACTION_BITS;
				// ...or is it better to use a shift
				int32_t deltaThreshold = ppmEMA >> (FAN_PPM_FRACTION_BITS + FAN_SCAN_PPM_DELTA_THRESHOLD_SHIFT);
				if (deltaThreshold == 0) deltaThreshold = 1;

				// Calculating the direction of change of the fan speed.
				// Some kind of velocity vector. In fact, it is a derivative of speed.
				// Or it can also be described as the grudge level
				// against sequential acceleration or deceleration )))
				int8_t lastDirection = ppmChangeDirection;
				if (deltaSum > deltaThreshold) {
					if (ppmChangeDirection < FAN_SCAN_PPM_GROWTH_GRUDGE) ppmChangeDirection += FAN_SCAN_PPM_GRUDGE_STEP;
				} else if (deltaSum < -deltaThreshold) {
					if (ppmChangeDirection > -FAN_SCAN_PPM_REDUCTION_GRUDGE) ppmChangeDirection -= FAN_SCAN_PPM_GRUDGE_STEP;
				} else {
					if (ppmChangeDirection > 0) {
						ppmChangeDirection -= FAN_SCAN_PPM_MERCY_STEP;
					} else if (ppmChangeDirection < 0) {
						ppmChangeDirection += FAN_SCAN_PPM_MERCY_STEP;
					}
				}

				// If the fan cyclically accelerates and decelerates,
				// then we can assume that it will continue to do so.
				if ((lastDirection * ppmChangeDirection) < 0) {
					directionChangesCount++;
					if (directionChangesCount >= FAN_SCAN_PPM_SPEED_DIRECTION_CHANGES_COUNT) {
						ppm = ppmEMA >> FAN_PPM_FRACTION_BITS;
						if (changeDirection) *changeDirection = 0;
						return (HAL_OK);
					}
				}

				// If the velocity vector is close to zero based on the results of several measurements...
				if ((pointsCount >= FAN_SCAN_PPM_MIN_POINTS) &&
					(abs(ppmChangeDirection) <= FAN_SCAN_PPM_JITTER)) {
					stablePointsCount++;

					// We must have several stable points in a row so that we can
					// reliably say that the rotation speed has stabilized.
					if ((stablePointsCount > FAN_SCAN_PPM_UNIFORM_CHANGES) &&
						(zeroesMap == 0)) {			// Also check if there was some zero points
						ppm = ppmEMA >> FAN_PPM_FRACTION_BITS;
						if (changeDirection) *changeDirection = 0;
						return (HAL_OK);
					}
				} else {
					if (stablePointsCount) stablePointsCount--;
				}

			} else {
				pointsCount++;
			}

		} while (0);

		previousPPM = currentPPM;
	}

	if (changeDirection) *changeDirection = ppmChangeDirection;
	return (HAL_TIMEOUT);
}


// Trying to find the minimum stable rotation speed using the binary search method
HAL_StatusTypeDef Fan_base::detectLowSpeedCapabilities(uint8_t mode, uint32_t topPPM, uint8_t & spinUpOption, uint32_t & minPPM, uint16_t & minControl) {
	// The procedure works for both PWM control and DC-DC control
	CALL_AND_CHECK(setServiceMode(mode));

	uint16_t bottom;
	uint16_t top;
	bool stablePointFound = false;

	// First, we'll try to spin-up with 25% of the control signal, then with 50%, etc.
	for (spinUpOption = Fan_Options_Spin_25; spinUpOption <= Fan_Options_Spin_100; spinUpOption++) {

		uint16_t spinUpControl = getSpinUpControlFromOption(spinUpOption);
		uint32_t ppm = 0;
		uint32_t lastTopPPM = 0;

		// Sometimes the rotor stops in a bad position and cannot
		// overcome inertia, despite the fact that in another position
		// it easily started rotating at the same control signal level.
		// If we have already found a stable point, then there is no sense
		// in starting the search again, even if we had to increase the level
		// of the control signal to spin-up.
		if (!stablePointFound) {
			top = spinUpControl;
			bottom = 0;
		}

		// We'll leave this cycle only in case of error or success
		while (1) {
			// Calculating the current control point for binary search
			uint16_t currentPoint = bottom + ((top - bottom) >> 1);

			// Spin-up if we stopped
			if (ppm == 0) CALL_AND_BREAK(spinUpNow(spinUpControl));

			// Set control value
			if (mode == Fan_Service_Mode_PWM) {
				CALL_AND_CHECK(setControlTarget(currentPoint, false, true));
			} else {
				CALL_AND_CHECK(refinePWRControlPoint(&currentPoint, 0));
			}


			// Wait for PPM stabilization
			int8_t changeDirection;
			HAL_StatusTypeDef result = waitRotationToStabilize(ppm, topPPM, Fan_Speed_Deceleration, &changeDirection);

			if (result != HAL_ERROR) {
				// Actually, we don't have to check whether the fan was slowing down or accelerating,
				// it's enough for us that the RPM is unstable.
				// But I'll leave this check for some future purposes.
				if ((result != HAL_TIMEOUT) && (ppm != 0) && (changeDirection >= 0) ) {
					top = currentPoint;
					stablePointFound = true;
					lastTopPPM = ppm;
				} else {
					bottom = currentPoint;
				}

			} else {
				// Try again in case of an error
				ppm = 0;
				break;
			}

			// Exit if the maximum accuracy limit is reached
			if ((top - bottom) <= FAN_SCAN_PPM_ACCURACY_LIMIT_STEPS) {

				// If the good minimum point has already been found...
				if (stablePointFound) {
					// ...then use it...

					// Even if the minimum point later turns out to be unstable,
					// we can easily fix it during operation (by adding
					// supplement steps to the minimum Control Value).
					minControl = top;
					minPPM = lastTopPPM;

					return (HAL_OK);
				}

				// ...or continue the search
				break;
			}
		}
	}

	return (HAL_TIMEOUT);
}


// Try to guess the fan model parameters
HAL_StatusTypeDef Fan_base::detectModelFeatures(uint8_t mode, uint8_t spinUpOption, uint32_t topPPM, uint32_t minPPM, uint16_t minControl, uint16_t & modelFeatures) {

	// The algorithm works relatively reliably in PWM and DC-DC Control Modes.
	bool pwmControlMode = (mode == Fan_Control_PWM);
	if ((serviceMode != mode) || (!pwmControlMode && (mode != Fan_Service_Mode_DCDC))) return (HAL_ERROR);

	// The algorithm requires some preliminary preparations.
	uint32_t ppm;
	CALL_AND_CHECK(getCrediblePPM(ppm));

	// Check if we need to spin-up for subsequent tests
	if (ppm == 0) {
		uint16_t spinUpControl = getSpinUpControlFromOption(spinUpOption);
		CALL_AND_CHECK(spinUpNow(spinUpControl));
	}

	// Just in case, let's add a little Control Value supplement
	// so that the fan doesn't stall.
	if (pwmControlMode) {
		minControl += FAN_SCAN_MINCONTROL_SUPPLEMENT_STEPS * FAN_PWM_CONTROL_STEP;
	} else {
		minControl += FAN_SCAN_MINCONTROL_SUPPLEMENT_STEPS * FAN_DCDC_CONTROL_STEP;
	}

	// Check if we need to set the minimum possible RPM
	if ((targetControl > minControl) || (currentControl > minControl)) {
		if (mode == Fan_Service_Mode_PWM) {
			CALL_AND_CHECK(setControlTarget(minControl, false, true));
		} else {
			CALL_AND_CHECK(refinePWRControlPoint(&minControl, 0));
		}

		if ((waitRotationToStabilize(ppm, topPPM, Fan_Speed_Deceleration) != HAL_OK) || (ppm == 0))
			return (HAL_ERROR);
	}

	uint16_t pulsePeriod, firstPulsePeriod, lastPulsePeriod, expectedPulsePeriod, pulsePeriodThreshold;

	// Prepare the driver to accurately measure the
	// tacho sensor pulse period (as much as possible).
	CALL_AND_CHECK(preparePulseMeasurement(&firstPulsePeriod, &topPPM, &expectedPulsePeriod));
	lastPulsePeriod = firstPulsePeriod;
	pulsePeriodThreshold = expectedPulsePeriod >> FAN_SCAN_PPM_DELTA_THRESHOLD_SHIFT;

	// Set control value immediately
	CALL_AND_CHECK(setControlTarget(FAN_CONTROL_TOP, true, false));

	uint32_t tickStart = HAL_GetTick();
	uint32_t tickEnd = tickStart;

	// Waiting for spinning-up to maximum speed
	while ((tickEnd - tickStart) < FAN_SCAN_SPIN_UP_TIMEOUT) {
		CALL_AND_CHECK(getPulsePeriod(pulsePeriod, &lastPulsePeriod));

		// Check if the rotation speed is increasing (pulse period decreasing)
		if ((pulsePeriod == firstPulsePeriod) ||
			((pulsePeriod < lastPulsePeriod) &&
			(pulsePeriod > expectedPulsePeriod) &&
			((pulsePeriod - expectedPulsePeriod) > pulsePeriodThreshold))) {

			tickEnd = HAL_GetTick();

		} else {
			break;
		}

		lastPulsePeriod = pulsePeriod;
	}

	uint32_t spinUpRatio, spinUpDuration = tickEnd - tickStart;
	CALL_AND_CHECK(getPPMFromPulsePeriod(pulsePeriod, ppm));

	if (spinUpDuration > 0) {
		// Some kind of acceleration relative rate
		spinUpRatio = ((ppm << FAN_SCAN_VELOCITY_RATIO_SHIFT) / minPPM) / spinUpDuration;
	} else {
		spinUpRatio = FAN_INDEFINITE_PPM;
	}

	// Wait for top PPM
	tickStart = HAL_GetTick();
	while ((ppm < topPPM) && ((HAL_GetTick() - tickStart) < FAN_SCAN_SPIN_UP_TIMEOUT)) {
		CALL_AND_CHECK(getPPM(ppm));
	}

	// Prepare the driver to accurately measure the tacho sensor pulse period
	CALL_AND_CHECK(preparePulseMeasurement(&firstPulsePeriod, &minPPM, &expectedPulsePeriod));
	lastPulsePeriod = firstPulsePeriod;

	// Set control value immediately
	CALL_AND_CHECK(setControlTarget(minControl, true, false));
	tickEnd = tickStart = HAL_GetTick();

	// Wait for the PPM to decrease to the minimum
	while ((tickEnd - tickStart) < FAN_SCAN_STOP_TIMEOUT) {
		CALL_AND_CHECK(getPulsePeriod(pulsePeriod, &lastPulsePeriod));

 		// Check if the rotation speed is decreasing (pulse period increasing)
		if ((pulsePeriod == firstPulsePeriod) ||
			((pulsePeriod > lastPulsePeriod) &&
			(pulsePeriod < expectedPulsePeriod) &&
			((expectedPulsePeriod - pulsePeriod) > pulsePeriodThreshold))) {

			tickEnd = HAL_GetTick();

		} else {
			break;
		}

		lastPulsePeriod = pulsePeriod;
	}

	uint32_t slowDownRatio, slowDownDuration = tickEnd - tickStart;
	CALL_AND_CHECK(getPPMFromPulsePeriod(pulsePeriod, ppm));

	if (slowDownDuration > 0) {
		// Some kind of deceleration relative rate
		slowDownRatio = ((topPPM << FAN_SCAN_VELOCITY_RATIO_SHIFT) / ppm) / slowDownDuration;
	} else {
		slowDownRatio = FAN_INDEFINITE_PPM;
	}

	/*
	For a more or less reliable assessment of the fan parameters, we need an ammeter.
	It is not available in this version of the board, so the evaluation function
	does not work reliably and can only be used as an experimental one.

	In the future, a current consumption measurement code there should be somewhere near
	this comment (namely, little above, immediately after the fan is spinned-up).

	In addition, to increase the accuracy of the assessment, it is necessary to increase
	the accuracy and speed of the tacho sensor pulses measuring.
	*/

	// Fan size detection
	bool spinUpSlow, slowDownSlow;
	// Here it would be great to take into account the current consumption
	// and make as many test measurements on different fan models as possible.
	if (mode == Fan_Service_Mode_PWM) {
		// TODO: Take into account the current consumption level!
		spinUpSlow = spinUpRatio < FAN_SCAN_SIZE_SPIN_THRESHOLD_PWM;
		slowDownSlow = slowDownRatio < FAN_SCAN_SIZE_STOP_THRESHOLD_PWM;
	} else {
		// TODO: Take into account the current consumption level!
		spinUpSlow = spinUpRatio < FAN_SCAN_SIZE_SPIN_THRESHOLD_DCDC;
		slowDownSlow = slowDownRatio < FAN_SCAN_SIZE_STOP_THRESHOLD_DCDC;
	}

	if (spinUpSlow == slowDownSlow) {
		modelFeatures |= Fan_Model_Size_Detected;
		if (slowDownSlow) modelFeatures |= Fan_Model_Size_Large;
	}

	// Fan class detection
	bool smoothSpeedChange = false;					// Server fans avoid instant shutdown or start-up
													// to reduce the load on the power system and extend bearing life.
	bool looksLikeServerFan = false;				// Built-in algorithm for smooth speed change should be the same
													// for both acceleration and deceleration.
													// Or the RPM should be high enough. The problem is that we can't
													// know the RPM without knowing the number of pulses per revolution (PPR).
													// Therefore, we can only assume. In general, this task is more suitable
													// for a neural network, and I just quickly sketched out a simple logic
													// that is unable to make good suggestions.
	// The server fan must have a PWM control
	if (mode == Fan_Service_Mode_PWM) {
		// TODO: Take into account the current consumption level!
		smoothSpeedChange = (spinUpDuration != 0) && (slowDownDuration != 0);

		// The tachometer should work during the rotor free-running (0% PWM)...
		// ...and acceleration rate should be approximately the same as the deceleration rate...
		// ...or the RPM should be high enough...
		uint32_t ratioDelta = spinUpRatio > slowDownRatio ? (spinUpRatio - slowDownRatio) : (slowDownRatio - spinUpRatio);
		looksLikeServerFan = ((modelFeatures & Fan_Model_PWM_Freewheel_Tachometer) != 0) &&
				((ratioDelta < ((spinUpRatio + slowDownRatio) >> 2)) || (getRPMFromPPM(topPPM) > FAN_SCAN_CLASS_RPM_THRESHOLD));

		/*
		Another idea for the future: high-quality fans should have a wide operating
		RPM (PPM) range, which we can estimate using the following formula:

		k = (PPM_MAX - PPM_MIN) / PPM_MAX
		or
		k = 1 - PPM_MIN / PPM_MAX

		Professional fans must have this coefficient of at least 0.9 (IMHO).
		An example of using the coefficient can be found in the function pidCanHandleDelta().
		*/
	}

	if (smoothSpeedChange == looksLikeServerFan) {
		modelFeatures |= Fan_Model_Class_Detected;
		if (looksLikeServerFan) modelFeatures |= Fan_Model_Class_Server;
	}

	return (HAL_OK);
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Public procedures

// Convert PPM to RPM according to the fan Pulses Per Revolution parameter
uint16_t Fan_base::getRPMFromPPM(uint32_t ppm) {
	uint8_t pprShift = (configuration == nullptr) ? Fan_Options_PPR_Default : configuration->options & Fan_Options_PPR_Mask;
	return ((uint16_t)(ppm >> pprShift));
}


// Get the latest measured RPM
uint16_t Fan_base::getRPM() {
	return (getRPMFromPPM(state.currentPPM));
}


// Get the latest measured voltage
uint16_t Fan_base::getVoltage() {
	return (state.currentVoltage);
}


// Get current Control Value scaled to the current fan Control basis
uint16_t Fan_base::getActualControl() {
	return (state.scaledControl);
}


// Set Control Mode (Disabled, PWM, DC-DC or MOSFET)
HAL_StatusTypeDef Fan_base::setControlMode(uint8_t mode) {
	if ((configuration == nullptr) || (state.status & Fan_Status_Fatal_Failure)) return (HAL_ERROR);

	mode &= Fan_Control_Mask;
	if (mode != state.controlMode) {

		if (!controlModeAvailable(mode, configuration)) return (HAL_ERROR);

		uint16_t realControl;
		CALL_AND_CHECK(getRealControlForMode(mode, realControl));
		targetControl = currentControl = realControl;

		// Just in case... if previous scanning process failed then clean the flag.
		// Also clean running flag to change the Control Mode
		state.status &= ~(Fan_Status_Scanning | Fan_Status_Warming | Fan_Status_PreWarming | Fan_Status_Running | Fan_Status_Spinning_Up |
						Fan_Status_Spin_Up_Failed | Fan_Status_PID_Suspended | Fan_Status_PID_Action_Required);
		state.controlMode = mode;
		resetRuntimeParameters();

		if (configuration->model & Fan_Model_Tachometer) {
			// Update the tachometer data for the correct operation of the fan start procedure.
			// This operation is not particularly important, so we can ignore the result.
			uint32_t ppm;
			getCrediblePPM(ppm);
		}

		return (updateControlValues());
	}

	// In any case let's reset the runtime min control correction.
	minControlSupplementSteps = 0;

	return (HAL_OK);
}


// Set Operation Mode (Normal, Manual, Silent, Turbo or Stopped)
HAL_StatusTypeDef Fan_base::setOperationMode(uint8_t mode) {
	if ((configuration == nullptr) || (state.status & Fan_Status_Fatal_Failure)) return (HAL_ERROR);

	mode &= Fan_Operation_Mask;
	if (mode != state.operationMode) {
		// In Turbo Mode the PID will be disabled
		if (mode == Fan_Operation_Turbo) state.status &= ~(Fan_Status_PID_Suspended | Fan_Status_PID_Action_Required);
		state.operationMode = mode;
		return (updateControlValues());
	}
	return (HAL_OK);
}


// Set the basis level of fan's power. If Control Basis = FAN_CONTROL_HALF_RANGE,
// then the fan will operate over the entire range of available speeds.
// By shifting the Basis down, we limit the maximum RPM, and by shifting it up,
// we force the fan to work in the upper part of its operating range.
HAL_StatusTypeDef Fan_base::setControlBasis(uint16_t controlBasis) {
	if ((configuration == nullptr) || (state.status & Fan_Status_Fatal_Failure)) return (HAL_ERROR);

	if (controlBasis != state.controlBasis) {
		state.controlBasis = controlBasis;
		return (updateControlValues());
	}
	return (HAL_OK);
}


// Set current Control Value (based on sensor readings,
// or manual setting in the control application).
HAL_StatusTypeDef Fan_base::setControl(uint16_t control, uint16_t supplement) {
	if ((configuration == nullptr) || (state.status & Fan_Status_Fatal_Failure)) return (HAL_ERROR);

	if ((control != state.controlValue) || (extraSupplement != supplement)) {
		state.controlValue = control;
		extraSupplement = supplement;
		return (updateControlValues());
	}
	return (HAL_OK);
}


// Temporary halt the fan (e.g. if the sensors are overcooled)
HAL_StatusTypeDef Fan_base::setHalted(bool halted) {
	if (halted) state.status |= Fan_Status_Halted; else
		state.status &= ~Fan_Status_Halted;
	return (HAL_OK);
}


// Reset fan errors, status PID parameters and timestamps
void Fan_base::resetState() {
	state.status = 0;
	state.totalErrorsCount = 0;
	lastUpdateTick = 0;
	hwErrorCounters = 0;
	fanErrorCounters = 0;
	resetRuntimeParameters();
	setServiceMode(Fan_Service_Mode_None);
}


// A critical error happened in the main program!
void Fan_base::kernelPanic() {
	configuration = nullptr;
	resetState();

	// Fast check available hardware
	uint16_t tmpVar;
	HAL_StatusTypeDef pwmResult = getPWM(tmpVar);
	HAL_StatusTypeDef pwrResult = getDCDC(tmpVar);
	HAL_StatusTypeDef mosfetResult = getMOSFET(tmpVar);

	// Turn on all the hardware to the maximum.
	// Here the result of the operation is no longer important to us.
	everythingOn100percent((mosfetResult == HAL_OK), (pwrResult == HAL_OK), (pwmResult == HAL_OK), true);
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Configuration procedures

// Determine the best Control Mode and store it in the new configuration
void Fan_base::chooseBestControlModeForConfig(Fan_Config * newConfig) {
	newConfig->controlMode = getBestControlMode(newConfig);
}


// Apply new or saved configuration
HAL_StatusTypeDef Fan_base::applyConfig(Fan_Config * newConfig, bool updateControlParameters) {
	if (newConfig == nullptr) return  (HAL_ERROR);

	// Clean the Fan Status
	resetState();

	// Check whether the configuration supports the Control Mode that it defines by default.
	if (!controlModeAvailable(newConfig->controlMode, newConfig)) {
		configuration = nullptr;
		state.status |= Fan_Status_Fatal_Failure;
		return (HAL_ERROR);
	}

	updateControlParameters |= (configuration == nullptr);
	configuration = newConfig;

	// Set initial control config
	if (updateControlParameters) {
		CALL_AND_CHECK(setControlMode(configuration->controlMode));
		CALL_AND_CHECK(setOperationMode(configuration->operationMode));
		CALL_AND_CHECK(setControlBasis(configuration->controlBasis));
	}

	return (HAL_OK);
}


// Scan available hardware features
HAL_StatusTypeDef Fan_base::scanHardware(Fan_Config * newConfig, UpdateScanningDelegate progressDelegate) {
	if (newConfig == nullptr) return (HAL_ERROR);

	state.status |= Fan_Status_Scanning;
	state.status &= ~Fan_Status_Running;
	newConfig->hardware = Fan_Hardware_Undefined;

	uint16_t tmpVar;
	uint8_t progress = 0;
	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Hardware, progress, newConfig);		// 0%

	// Prepare the hardware for testing.
	// We cannot know what changes have occurred to the equipment and fan
	// since the previous check (if any), so we must be careful
	// and avoid unnecessary transients.

	// Check if PWM generator available and set it to 0
	HAL_StatusTypeDef pwmResult = getPWM(tmpVar);
	if ((pwmResult == HAL_OK) && (tmpVar != 0)) {
		pwmResult = setServiceMode(Fan_Service_Mode_PWM);
		CHECK_AND_CALL(setControlTarget(0, false, true), pwmResult);
	}

	// Check DC-DC and MOSFET available
	HAL_StatusTypeDef pwrResult = getDCDC(tmpVar);
	HAL_StatusTypeDef mosfetResult = getMOSFET(tmpVar);
	if (mosfetResult == HAL_OK) {
		// Let's check if the MOSFET is available and turned off
		// and if so, set the DC-DC output voltage to a minimum
		// to avoid large transients when turned on.
		if ((tmpVar == 0) && (pwrResult == HAL_OK)) {
			pwrResult = setServiceMode(Fan_Service_Mode_DCDC);
			CHECK_AND_CALL(setControlTarget(0, true, false), pwrResult);
		}

		mosfetResult = setServiceMode(Fan_Service_Mode_MOSFET);
		CHECK_AND_CALL(setControlTarget(FAN_CONTROL_TOP, true, false), mosfetResult);
	}


	// Now we can continue...
	HAL_StatusTypeDef rpmResult = getPulsePeriod(tmpVar);			// Check if RPM measurement HW/SW available
	HAL_StatusTypeDef voltmeterResult = getVoltage(tmpVar);			// Check if voltmeter available
	if (voltmeterResult != HAL_OK) pwrResult = voltmeterResult;		// Complex status for power Control Mode (DC-DC + VMeter)

	// Let's start the DC-DC testing from the top (high voltage) point:
	int8_t steps = -FAN_SCAN_MAX_STEPS;
	uint16_t controlPoint = (uint16_t)(FAN_CONTROL_FULL_RANGE - FAN_DCDC_CONTROL_STEP);

	while ((pwrResult == HAL_OK) && (steps != 0)) {
		// Prepare DC-DC top point
		pwrResult = refinePWRControlPoint(&controlPoint, 0);
		// Search for the starting point of fall
		CHECK_AND_CALL(refinePWRControlPoint(&controlPoint, steps), pwrResult);
		steps /= 2;
		progress += 10;
		CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Hardware, progress, newConfig);	// 10% - 30%
	}

	// Stabilize the voltage at the found point
	CHECK_AND_CALL(refinePWRControlPoint(&controlPoint, 0), pwrResult);
	progress += 10;
	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Hardware, progress, newConfig);		// 40%

	// Here we must save the voltage regardless of the results of the DC-DC operation.
	// We will need it to check the MOSFET.
	if (voltmeterResult == HAL_OK) {
		voltmeterResult = getVoltage(tmpVar);
		newConfig->maxVoltage = tmpVar;
		newConfig->maxVoltageControl = controlPoint - 1 + FAN_DCDC_CONTROL_STEP;
	} else {
		newConfig->maxVoltage = 0;
		newConfig->maxVoltageControl = 0;
	}

	// Next, the bottom point:
	steps = FAN_SCAN_MAX_STEPS;
	controlPoint = 0;

	while ((pwrResult == HAL_OK) && (steps != 0)) {
		// Prepare DC-DC bottom point
		pwrResult = refinePWRControlPoint(&controlPoint, 0);
		// Search for the starting point of growth
		CHECK_AND_CALL(refinePWRControlPoint(&controlPoint, steps), pwrResult);
		steps /= 2;
		progress += 10;
		CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Hardware, progress, newConfig);	// 50% - 70%
	}

	// Stabilize the voltage at the found point
	CHECK_AND_CALL(refinePWRControlPoint(&controlPoint, 0), pwrResult);
	progress += 10;
	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Hardware, progress, newConfig);		// 80%

	if (pwrResult == HAL_OK) {
		getVoltage(tmpVar);			// Don't need to check the result after refinePWRControlPoint() call;
		newConfig->minVoltage = tmpVar;
		newConfig->minVoltageControl = controlPoint;
	} else {
		newConfig->minVoltage = 0;
		newConfig->minVoltageControl = 0;
	}

	// Check DC-DC voltage range
	if (pwrResult == HAL_OK) {
		pwrResult = ((newConfig->maxVoltage - newConfig->minVoltage) > (FAN_SCAN_MIN_VOLTAGE_RANGE << voltageFractionBits())) ? HAL_OK : HAL_ERROR;
	}

	// Check the MOSFET operation
	if ((mosfetResult == HAL_OK) && (voltmeterResult == HAL_OK)) {
		uint16_t mosfetOnVoltage = tmpVar;

		// If the minimum voltage on the DC-DC is too low to detect the MOSFET,
		// then first set the voltage to the desired level.
		if ((mosfetOnVoltage < (FAN_SCAN_MIN_MOSFET_DETECTION_VOLTAGE << voltageFractionBits())) &&
				(pwrResult == HAL_OK)) {

			controlPoint = Fan_base::mapUInt(FAN_SCAN_MIN_MOSFET_DETECTION_VOLTAGE << voltageFractionBits(),
					newConfig->minVoltage, newConfig->maxVoltage, newConfig->minVoltageControl, newConfig->maxVoltageControl);
			pwrResult = refinePWRControlPoint(&controlPoint, 0);
		}

		progress += 10;
		CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Hardware, progress, newConfig);	// 90%

		// Turn the MOSFET off
		mosfetResult = setServiceMode(Fan_Service_Mode_MOSFET);
		CHECK_AND_CALL(setControlTarget(0, true, false), mosfetResult);
		// We will track the voltage drop in the refinePWRControlPoint() procedure,
		// but just in case, let's make a small delay.
		HAL_Delay(FAN_SCAN_VOLTAGE_TIME_CONSTANT);

		// Wait for voltage stabilization after switching GPIO off
		if ((mosfetResult == HAL_OK) && (refinePWRControlPoint(nullptr, 0) == HAL_OK)) {
			voltmeterResult = getVoltage(tmpVar);
			// Check ON/OFF voltage range
			mosfetResult = (mosfetOnVoltage >> 1) > tmpVar ? HAL_OK : HAL_ERROR;
		}
	}

	// Additional voltmeter check
	if ((newConfig->maxVoltage > (FAN_SCAN_MAX_VOLTAGE << voltageFractionBits())) ||
		(newConfig->maxVoltage < (FAN_SCAN_MIN_VOLTAGE << voltageFractionBits())) ) voltmeterResult = HAL_ERROR;

	// Set up configuration
	if (pwmResult == HAL_OK) newConfig->hardware |= Fan_Hardware_PWM_Available;
	if (rpmResult == HAL_OK) newConfig->hardware |= Fan_Hardware_RPM_Available;
	if (voltmeterResult == HAL_OK) {
		// We can't test DC-DC and MOSFET without a voltmeter, which means we won't use them.
		newConfig->hardware |= Fan_Hardware_Voltmeter_Available;
		if (mosfetResult == HAL_OK) newConfig->hardware |= Fan_Hardware_MOSFET_Available;
		if (pwrResult == HAL_OK) newConfig->hardware |= Fan_Hardware_DCDC_Available;
	}

	// Clean the Fan_Status_Scanning state/flag
	state.status &= ~Fan_Status_Scanning;

	setServiceMode(Fan_Service_Mode_None);
	progress = 100;
	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Hardware, progress, newConfig);		// 100%

	return (HAL_OK);
}


// Start the fan warming-up at 50% power.
HAL_StatusTypeDef Fan_base::prewarmFan(Fan_Config * newConfig, UpdateScanningDelegate progressDelegate) {
	// In this board revision (1.0b) we can determine the presence
	// of a fan only by the tacho sensor.
	if ((newConfig->hardware & Fan_Hardware_RPM_Available) == 0) return (HAL_OK);

	// Indicate the start of the warming-up
	state.status |= Fan_Status_PreWarming;
	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_PreWarm, 0, newConfig);

	bool mosfetHardwareAvailable = (newConfig->hardware & Fan_Hardware_MOSFET_Available) != 0;
	bool dcdcHardwareAvailable = (newConfig->hardware & Fan_Hardware_DCDC_Available) != 0;
	bool pwmHardwareAvailable = (newConfig->hardware & Fan_Hardware_PWM_Available) != 0;

	// Turn on all the hardware to the maximum
	CALL_AND_CHECK(everythingOn100percent(mosfetHardwareAvailable, dcdcHardwareAvailable, pwmHardwareAvailable));

	uint32_t topPPM, ppm;
	if (spinUpNow(FAN_CONTROL_TOP, &topPPM) == HAL_OK) {

		if (pwmHardwareAvailable) {
			// In this case, the Service Mode should already be set to PWM in the everythingOn100percent procedure,
			// so set PWM to 50%.
			CALL_AND_CHECK(setControlTarget(FAN_CONTROL_HALF_RANGE, false, true));
			HAL_Delay(FAN_SCAN_SLOW_DOWN_TIMEOUT);
			CALL_AND_CHECK(getCrediblePPM(ppm));

			if ((ppm != 0) && (topPPM > ppm) &&
				((topPPM - ppm) > (topPPM >> FAN_SCAN_PPM_DELTA_THRESHOLD_SHIFT))) {
				// Most likely, the fan supports PWM, so we'll leave it to warm up.
				return (HAL_OK);
			}
		}
	}

	if (dcdcHardwareAvailable) {
		// We can only get here if we don't have PWM hardware
		// or the fan isn't connected or doesn't support PWM control.

		// So let's revert the PWM to 100%, just in case...
		if (pwmHardwareAvailable) {
			CALL_AND_CHECK(setControlTarget(FAN_CONTROL_TOP, false, true));
		}

		// ...set the service mode to DC-DC and reduce the voltage to 50%.
		CALL_AND_CHECK(setServiceMode(Fan_Service_Mode_DCDC));

		// The supply voltage may not match the adjustment range.
		// In addition, the adjustment range can be shifted towards zero
		// or towards the maximum voltage. Therefore, it is better to
		// calculate the Control Value for the half of power supply voltage.
		uint16_t halfVoltage = newConfig->maxVoltage >> 1;
		uint16_t halfVoltageControl = (halfVoltage < newConfig->minVoltage) ? newConfig->minVoltageControl :
				(uint16_t)Fan_base::mapUInt(newConfig->maxVoltage >> 1,
				newConfig->minVoltage, newConfig->maxVoltage,
				newConfig->minVoltageControl, newConfig->maxVoltageControl);
		//CALL_AND_CHECK(setControlTarget(FAN_CONTROL_HALF_RANGE, false, true));
		CALL_AND_CHECK(setControlTarget(halfVoltageControl, false, true));
	}

	// There's nothing more we can do. If there is DC-DC hardware, the fan will remain
	// at 50% supply voltage. If there is no DC-DC, it will be powered to maximum.
	// Even if the fan does not have a tachometer, we will not turn it off completely
	// to ensure normal cooling and normal system operation during the scan.

	return (HAL_OK);
}


// Scan the available fan features
HAL_StatusTypeDef Fan_base::scanFan(Fan_Config * newConfig, UpdateScanningDelegate progressDelegate, uint32_t prewarmingDuration) {
	if (newConfig == nullptr) return (HAL_ERROR);

	// Reset configuration
	newConfig->model = Fan_Model_Unconnected;
	/* Common Fan metrics */
	newConfig->maxPPM = 0;
	/* PWM metrics */
	newConfig->minPWMControl = 0;
	newConfig->pwmControlledMinPPM = 0;
	newConfig->pwmPenultimatePPM = 0;
	/* Power Fan metrics */
	newConfig->minPWRControl = 0;
	newConfig->pwrControlledMinPPM = 0;

	// In this board revision (1.0b) we can determine
	// the presence of a fan only by the tacho sensor.
	if ((newConfig->hardware & Fan_Hardware_RPM_Available) == 0) return (HAL_OK);

	// TODO Think about a current consumption sensor

	bool mosfetHardwareAvailable = (newConfig->hardware & Fan_Hardware_MOSFET_Available) != 0;
	bool dcdcHardwareAvailable = (newConfig->hardware & Fan_Hardware_DCDC_Available) != 0;
	bool pwmHardwareAvailable = (newConfig->hardware & Fan_Hardware_PWM_Available) != 0;
	bool classDetected = false;
	bool errorOccurred = false;
	uint32_t ppm;
	uint32_t warmingUpDuration;

	// If there is no available hardware, then there is nothing to scan.
	// So we can successfully return.
	if (!(mosfetHardwareAvailable || dcdcHardwareAvailable || pwmHardwareAvailable)) {
		goto finished;
	}

	state.status |= Fan_Status_Warming;
	state.status &= ~(Fan_Status_Running | Fan_Status_PreWarming);

	// Indicate the start of the warming-up
	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_WarmUp, 0, newConfig);				// 0% - warm up the fan

	// Turn on all the hardware to the maximum
	CALL_AND_CHECK(everythingOn100percent(mosfetHardwareAvailable, dcdcHardwareAvailable, pwmHardwareAvailable));

	// Check the duration of the prewarming
	warmingUpDuration = FAN_SCAN_SHORT_WARM_UP_TIME;
	if (prewarmingDuration < FAN_SCAN_ADDITIONAL_WARM_UP_TIME) {
		warmingUpDuration += (FAN_SCAN_ADDITIONAL_WARM_UP_TIME - prewarmingDuration);
	}

	// Warm up the fan
	CALL_AND_CHECK(warmUpAndGetPPM(warmingUpDuration, ppm));
	if (ppm == 0) {
		// No fan connected
		goto finished;
	}

	state.status |= Fan_Status_Scanning;
	state.status &= ~Fan_Status_Warming;

	// Indicate the start of the scan task
	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Fan, 10, newConfig);					// 10%

	// We need to check if the fan speed is stable or if
	// there is some kind of noise coming through the tacho sensor line:
	CALL_AND_CHECK(waitRotationToStabilize(ppm, ppm, Fan_Speed_No_Change));

	// Here we can determine the presence of a fan only by the tacho sensor,
	// but theoretically we can rely on the readings of some current sensor.
	newConfig->model |= (Fan_Model_Connected | Fan_Model_Tachometer);
	newConfig->maxPPM = ppm;

	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Fan, 15, newConfig);					// 15%

	do { // Multiple "goto" replacement
		if (pwmHardwareAvailable) {

			// Check if there is a significant decrease in fan speed
			// if we switch to 99% (or so) instead of 100% of the PWM duty cycle.
			CALL_ACCUMULATE_ERROR_AND_BREAK(setServiceMode(Fan_Service_Mode_PWM), errorOccurred);
			CALL_ACCUMULATE_ERROR_AND_BREAK(setControlTarget(FAN_CONTROL_TOP - FAN_SCAN_PWM_EXCESSIVE_FS_STEP, false, true), errorOccurred);

			uint32_t penultimatePPM = 0;
			if (waitRotationToStabilize(ppm, ppm, Fan_Speed_Deceleration) == HAL_OK) penultimatePPM = ppm;

			CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Fan, 20, newConfig);			// 20%

			// Let's check whether the signal from the
			// tachometer sensor turns off when the PWM is set to 0%
			uint16_t pulsePeriod, lastPulsePeriod, expectedPulsePeriod;

			// Prepare the driver to accurately measure the
			// tacho sensor pulse period (as much as possible).
			ppm = 0;
			CALL_ACCUMULATE_ERROR_AND_BREAK(preparePulseMeasurement(&lastPulsePeriod, &ppm, &expectedPulsePeriod), errorOccurred);

			// Set PWM to 0%
			CALL_ACCUMULATE_ERROR_AND_BREAK(setControlTarget(0, false, true), errorOccurred);

			// Immediately check the period of the tacho pulses
			CALL_ACCUMULATE_ERROR_AND_BREAK(getPulsePeriod(pulsePeriod, &lastPulsePeriod), errorOccurred);

			if (pulsePeriod < expectedPulsePeriod) {
				// If the signal from the tachometer sensor does not stop instantly,
				// then we can wait for the speed to stabilize using its readings.
				CALL_ACCUMULATE_ERROR_AND_BREAK(waitRotationToStabilize(ppm, newConfig->maxPPM, Fan_Speed_Deceleration), errorOccurred);
				// Let's reuse expectedPulsePeriod variable as boolean.
				// Just to save memory.
				expectedPulsePeriod = 0;

			} else {
				// If the period is maximum, then the pulses have stopped.
				// Some fans produce zero tachometer readings as soon
				// as we set PWM signal with a fairly low duty cycle.
				// However, they can still rotate due to the inertia of the rotor.
				// Therefore, we have no other way to make sure that they stop,
				// except to wait a sufficient amount of time.
				HAL_Delay(FAN_SCAN_STOP_TIMEOUT);
			}

			CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Fan, 30, newConfig);			// 30%

			// Check if the PWM control is working
			if (ppm < (newConfig->maxPPM >> FAN_SCAN_PWM_DETECTION_RANGE_SHIFT)) {

				// PWM Control available
				newConfig->model |= Fan_Model_PWM;
				if (expectedPulsePeriod == 0) newConfig->model |= Fan_Model_PWM_Freewheel_Tachometer;

				if (ppm == 0) {
					uint8_t spinUpOption;
					uint16_t minControl;
					uint32_t minPPM;

					// TODO Take a few points (ideally 12-16 pieces) and build an approximation from them.

					// Try to detect minimally stable fan speed in PWM control mode
					if (detectLowSpeedCapabilities(Fan_Service_Mode_PWM, newConfig->maxPPM, spinUpOption, minPPM, minControl) == HAL_OK) {
						newConfig->minPWMControl = minControl;
						newConfig->pwmControlledMinPPM = minPPM;
						// Clear fan Spin-up option...
						newConfig->options &= ~(Fan_Options_Spin_Mask << Fan_Options_Spin_PWM_Bit_Shift);
						// ...and set the detected one
						newConfig->options |= (spinUpOption << Fan_Options_Spin_PWM_Bit_Shift);
						newConfig->options |= (1 << Fan_Options_PID_Enabled_PWM_Bit_Shift);
						newConfig->model |= Fan_Model_PWM_Range_Detected;

						// Try to assume model features
						if ((Fan_base::configFlags & Fan_Config_Flag_Fan_Model_Detection) != 0) {
							uint16_t modelFeatures = newConfig->model;
							if (detectModelFeatures(Fan_Service_Mode_PWM, spinUpOption, newConfig->maxPPM, minPPM, minControl, modelFeatures) == HAL_OK) {
								newConfig->model |= modelFeatures;
								if (modelFeatures & (Fan_Model_Class_Server | Fan_Model_Size_Large)) newConfig->options |= (1 << Fan_Options_PID_Slow_PWM_Bit_Shift);
								classDetected = true;
							}
						}
					}

				} else {
					// newConfig->minPWMControl = 0;		// We have already done it.
					newConfig->pwmControlledMinPPM = ppm;
					// Clear fan spin-up option
					newConfig->options &= ~(Fan_Options_Spin_Mask << Fan_Options_Spin_PWM_Bit_Shift);
					// This operation won't change anything since Fan_Options_Spin_0 = 0,
					// but I'll leave it anyway in case of any changes.
					newConfig->options |= (Fan_Options_Spin_0 << Fan_Options_Spin_PWM_Bit_Shift);
					newConfig->options |= (1 << Fan_Options_PID_Enabled_PWM_Bit_Shift);
					newConfig->model |= Fan_Model_PWM_Range_Detected;

					// Try to assume model features
					if ((Fan_base::configFlags & Fan_Config_Flag_Fan_Model_Detection) != 0) {
						uint16_t modelFeatures = newConfig->model;
						if (detectModelFeatures(Fan_Service_Mode_PWM, Fan_Options_Spin_0, newConfig->maxPPM, ppm, 0, modelFeatures) == HAL_OK) {
							newConfig->model |= modelFeatures;
							if (modelFeatures & (Fan_Model_Class_Server | Fan_Model_Size_Large)) newConfig->options |= (1 << Fan_Options_PID_Slow_PWM_Bit_Shift);
							classDetected = true;
						}
					}
				}

				if ((newConfig->model & Fan_Model_PWM_Range_Detected) != 0) {

					if ((penultimatePPM != 0) &&
						(newConfig->maxPPM > penultimatePPM) &&
						(((newConfig->maxPPM - penultimatePPM) * 100 / (newConfig->maxPPM - newConfig->pwmControlledMinPPM)) >
					FAN_SCAN_PWM_EXCESSIVE_FS_PERCENT)) {
						newConfig->model |= Fan_Model_PWM_Excessive_Full_Speed;
						newConfig->pwmPenultimatePPM = penultimatePPM;
					}

					// In Normal Mode, if the fan supports PWM control, then we will only use it.
					// In Professional Mode, we will continue scanning in DC-DC control mode (if available)
					// so that we can switch to it later in case of PWM errors.
					if ((Fan_base::configFlags & Fan_Config_Flag_Professional_Mode) == 0) {
						// Fuck it! A little GOTO won't kill me!
						goto finished;
					}
				}
			}
		}

	} while (0); // "goto" replacement


	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Fan, 60, newConfig);					// 60%

	do { // Multiple "goto" replacement

		if (dcdcHardwareAvailable) {
			// Prepare the hardware for DC-DC Control testing
			// First, stop the fan
			CALL_ACCUMULATE_ERROR_AND_BREAK(setServiceMode(Fan_Service_Mode_DCDC), errorOccurred);
			CALL_ACCUMULATE_ERROR_AND_BREAK(setControlTarget(0, false, true), errorOccurred);

			if (mosfetHardwareAvailable) {
				CALL_ACCUMULATE_ERROR_AND_BREAK(setServiceMode(Fan_Service_Mode_MOSFET), errorOccurred);
				CALL_ACCUMULATE_ERROR_AND_BREAK(setControlTarget(0, true, false), errorOccurred);
			}

			// We have no way of determining whether the fan that is not powered
			// has finished rotating. So we'll just wait to be sure the fan is stopped.
			HAL_Delay(FAN_SCAN_STOP_TIMEOUT);

			CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Fan, 65, newConfig);			// 65%

			// Set PWM to 100%
			if (pwmHardwareAvailable) {
				CALL_ACCUMULATE_ERROR_AND_BREAK(setServiceMode(Fan_Service_Mode_PWM), errorOccurred);
				CALL_ACCUMULATE_ERROR_AND_BREAK(setControlTarget(FAN_CONTROL_TOP, true, false), errorOccurred);
			}

			// And turn MOSFET on
			if (mosfetHardwareAvailable) {
				CALL_ACCUMULATE_ERROR_AND_BREAK(setServiceMode(Fan_Service_Mode_MOSFET), errorOccurred);
				CALL_ACCUMULATE_ERROR_AND_BREAK(setControlTarget(FAN_CONTROL_TOP, true, false), errorOccurred);
			}


			// If the fan speed is unstable or zero...
			if ((waitRotationToStabilize(ppm, newConfig->maxPPM, Fan_Speed_Acceleration) != HAL_OK) || (ppm == 0)) {

				CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Fan, 70, newConfig);		// 70%

				uint8_t spinUpOption;
				uint16_t minControl;
				uint32_t minPPM;

				// TODO Take a few points (ideally 12-16 pieces) and build an approximation from them.

				// ...try to detect minimally stable fan speed in DC-DC control mode.
				if (detectLowSpeedCapabilities(Fan_Service_Mode_DCDC, newConfig->maxPPM, spinUpOption, minPPM, minControl) == HAL_OK) {
					newConfig->minPWRControl = minControl;
					newConfig->pwrControlledMinPPM = minPPM;
					// Clear fan spin-up option
					newConfig->options &= ~(Fan_Options_Spin_Mask << Fan_Options_Spin_PWR_Bit_Shift);
					newConfig->options |= (spinUpOption << Fan_Options_Spin_PWR_Bit_Shift);
					newConfig->options |= (1 << Fan_Options_PID_Enabled_PWR_Bit_Shift);
					newConfig->model |= Fan_Model_DCDC_Range_Detected;

					// Try to assume model features
					if (((Fan_base::configFlags & Fan_Config_Flag_Fan_Model_Detection) != 0) && !classDetected) {
						uint16_t modelFeatures = newConfig->model;
						if (detectModelFeatures(Fan_Service_Mode_DCDC, spinUpOption, newConfig->maxPPM, minPPM, minControl, modelFeatures) == HAL_OK) {
							newConfig->model |= modelFeatures;
							if (modelFeatures & Fan_Model_Size_Large) newConfig->options |= (1 << Fan_Options_PID_Slow_PWR_Bit_Shift);
						}
					}
				}

			} else {
				// newConfig->minPWMControl = 0;		// We have already done it.
				newConfig->pwrControlledMinPPM = ppm;
				// Clear fan spin-up option
				newConfig->options &= ~(Fan_Options_Spin_Mask << Fan_Options_Spin_PWR_Bit_Shift);
				// This operation won't change anything since Fan_Options_Spin_0 = 0,
				// but I'll leave it anyway in case of any changes.
				newConfig->options |= (Fan_Options_Spin_0 << Fan_Options_Spin_PWR_Bit_Shift);
				newConfig->options |= (1 << Fan_Options_PID_Enabled_PWR_Bit_Shift);
				newConfig->model |= Fan_Model_DCDC_Range_Detected;

				// Try to assume model features
				if (((Fan_base::configFlags & Fan_Config_Flag_Fan_Model_Detection) != 0) && !classDetected) {
					uint16_t modelFeatures = newConfig->model;
					if (detectModelFeatures(Fan_Service_Mode_DCDC, Fan_Options_Spin_0, newConfig->maxPPM, ppm, 0, modelFeatures) == HAL_OK) {
						newConfig->model |= modelFeatures;
						if (modelFeatures & Fan_Model_Size_Large) newConfig->options |= (1 << Fan_Options_PID_Slow_PWR_Bit_Shift);
					}
				}
			}
		}

	} while (0); // "goto" replacement


// You haven't seen it!
finished:

	// Clean the Fan_Status_Scanning state/flag
	state.status &= ~(Fan_Status_Scanning | Fan_Status_Warming | Fan_Status_PreWarming);

	setServiceMode(Fan_Service_Mode_None);
	CALL_PROGRESS_DELEGATE(progressDelegate, Fan_Scan_Task_Fan, 100, newConfig);				// 100%

	return (errorOccurred ? HAL_ERROR : HAL_OK);
}


// Set the fan to 50% power while scanning others or for some other long tasks.
HAL_StatusTypeDef Fan_base::leaveAt50Percent(Fan_Config * newConfig) {
	if ((newConfig == nullptr) || (newConfig->controlMode == Fan_Control_Disabled)) return (HAL_ERROR);

	CALL_AND_CHECK(setServiceMode(newConfig->controlMode));
	bool pwmControlMode = (serviceMode == Fan_Service_Mode_PWM);		// The fan is in PWM Control Mode

	uint16_t spinUpControl = getSpinUpControlFromOption(newConfig->options,
			pwmControlMode ? Fan_Options_Spin_PWM_Bit_Shift : Fan_Options_Spin_PWR_Bit_Shift);
	uint32_t ppm;
	CALL_AND_CHECK(spinUpNow(spinUpControl, &ppm));

	if (ppm != 0) {
		uint16_t controlValue = FAN_CONTROL_TOP;
		if (pwmControlMode) {
			// Now we know the operating ranges of the fan
			// and we can just take the average value.
			controlValue = newConfig->minPWMControl + ((FAN_CONTROL_TOP - newConfig->minPWMControl) >> 1);
		} else if (serviceMode == Fan_Service_Mode_DCDC) {
			// The same for voltage
			controlValue = newConfig->minVoltageControl + ((newConfig->maxVoltageControl - newConfig->minVoltageControl) >> 1);
		}

		CALL_AND_CHECK(setControlTarget(controlValue, false, false));
	}

	return (HAL_OK);
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Periodic execution procedures

// Perform increments to smoothly increase/decrease the PWM or DC-DC voltage.
// For normal operation, you must call this procedure 10-1000 times per second.
HAL_StatusTypeDef Fan_base::doIncrements() {
	int32_t delta = targetControl;
	delta -= currentControl;

	if (delta != 0) {
		int32_t rocValue = (configuration == nullptr) ? Fan_Options_ROC_Default :
				(configuration->options >> Fan_Options_ROC_Bit_Shift) & Fan_Options_ROC_Mask;
		rocValue = 1 << rocValue;

		// currentControl = 12345
		// targetControl = 23456
		// delta = 11111
		// rocValue = 256
		// 11111 < -256 = false
		// 11111 > 256 = true => delta = 256
		//
		// currentControl = 23456
		// targetControl = 12345
		// delta = -11111
		// rocValue = 256
		// -11111 < -256 = true => delta = -256
		// -11111 > 256 = false

		if (delta < -rocValue) delta = -rocValue; else
			if (delta > rocValue) delta = rocValue;

		// The Current Control Value is updated only if the hardware has accepted
		// the new value. Otherwise, the step will be repeated on the next call
		// (e.g. if the I2C bus is busy with a transfer from the main thread).
		uint16_t newControl = (uint16_t)(currentControl + delta);
		HAL_StatusTypeDef result = driverSetControl(newControl, false);
		if (result == HAL_OK) currentControl = newControl;
		return (result);
	}

	return (HAL_OK);
}


// On/off operations, Control Mode changes, voltage and PPM checks,
// PID control and error checking.
// Returns "true" if the status flags contain any errors or warnings.
// ATTENTION! For more reliable work, it is advisable to perform
// all changes to the Operating Mode, Control Value and Control Basis
// immediately AFTER calling this procedure, and not before it.
bool Fan_base::doBasicOperations() {
	if (configuration == nullptr) return (false);

	// Hardware capabilities
	bool mosfetHardwareAvailable = (configuration->hardware & Fan_Hardware_MOSFET_Available) != 0;
	bool dcdcHardwareAvailable = (configuration->hardware & Fan_Hardware_DCDC_Available) != 0;
	bool pwmHardwareAvailable = (configuration->hardware & Fan_Hardware_PWM_Available) != 0;
	bool tachHardwareAvailable = (configuration->hardware & Fan_Hardware_RPM_Available) != 0;
	bool vmeterHardwareAvailable = (configuration->hardware & Fan_Hardware_Voltmeter_Available) != 0;

	// Read basic parameters (I hope that in the next version
	// there will also be a reading of current consumption)
	if (tachHardwareAvailable) {
		uint32_t ppmBuffer;
		// We can ignore errors here and below,
		// as they will all be reflected in the current state.
		getPPM(ppmBuffer);
		state.currentPPM = ppmBuffer;
	}

	if (vmeterHardwareAvailable) {
		uint16_t voltageBuffer;
		// Ignore the error again. In case of any problems,
		// the reaction to errors will lead to a change
		// in the Control Mode (at the bottom of this function).
		getVoltage(voltageBuffer);
		state.currentVoltage = voltageBuffer;
	}

	// TODO Read current consumption

	// The next steps don't make sense if we don't know
	// the hardware and fan configuration or if we in scanning mode
	// or the fan is irreparably failed...
	if ((configuration != nullptr) &&
		((state.status & Fan_Status_Scanning) == 0) &&
		((state.status & Fan_Status_Fatal_Failure) == 0)) {

		bool fanRunning = (state.status & Fan_Status_Running) != 0;			// The fan is running and isn't in the process of stopping
		bool fanLogicInProgress = false;									// Fan's on/off subroutines are in progress
		bool spinUpRequired = false;										// The fan rotor needs to be spinned-up
		bool setFailure = false;											// +1 to set the fan to the Failed status
		bool failureSeverity = false;										// Set the fan as Failed immediately!
		uint8_t errorPressureSteps = 0;										// If the errors are fixable, but follow one another for a
																			// short time, we also need to set the fan to the Failed state.
		bool pwmControlMode = state.controlMode == Fan_Control_PWM;			// The fan is in PWM Control Mode
		bool mosfetControlMode = state.controlMode == Fan_Control_MOSFET;	// The fan is in MOSFET Control Mode

		// Minimum PPM for PWM and DC-DC Control Modes
		// (if the MOSFET mode is selected, this parameter won't be used).
		uint32_t minPPM = pwmControlMode ? configuration->pwmControlledMinPPM : configuration->pwrControlledMinPPM;


		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// FAN'S ON/OFF LOGIC (STATE MACHINE)
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

		// Fan switching stop/start/spin-up time-distributed algorithms
		do { // Multiple "goto" replacement

			if ((state.controlMode == Fan_Control_Disabled) ||
				// Let's take into account the possibility that the fan must always be on
				(((state.status & Fan_Status_Halted) != 0) && ((configuration->options & (1 << Fan_Options_Always_On_Shift)) == 0))) {

				// Fan stop automation
				if (fanRunning || ((state.status & Fan_Status_Switched_Off) == 0)) {
					// Fan switching off time-distributed algorithm

					// A flag indicating that the fan stop or start (including spin-up) routine is active
					fanLogicInProgress = true;

					// The fan is no longer considered running from the very first step of the stopping
					// sequence. This allows the starting branch to take over at any step if the fan
					// has to be started again before it is completely switched off.
					state.status &= ~(Fan_Status_Running | Fan_Status_Spinning_Up);
					fanRunning = false;

					// Step 0:
					// Checking whether the equipment is turned off
					// so as not to take unnecessary steps.
					bool mosfetOff = true;
					bool dcdcOff = true;
					bool pwmOff = true;

					// Check if hardware already turned off (and again, it's okay if an error occurs here,
					// we can ignore it and analyze it later).
					if (mosfetHardwareAvailable) checkControlEqualsForMOSFET(0, mosfetOff);
					if (dcdcHardwareAvailable) checkControlEqualsForDCDC(0, dcdcOff);
					if (pwmHardwareAvailable) checkControlEqualsForPWM(0, pwmOff);

					// Step 1:
					// If we are not yet at the stage of waiting for the 0% PWM output...
					if ((serviceMode != Fan_Service_Mode_PWM) && !pwmOff && pwmHardwareAvailable) {
						// If PWM isn't supported by the fan
						bool immediately = ((configuration->model & Fan_Model_PWM) == 0);

						// Set PWM to 0%
						setServiceMode(Fan_Service_Mode_PWM);
						setControlTarget(0, immediately, false);
						pwmOff = immediately;
					}

					// Step 2:
					// If we are in PWM mode, then let's calmly wait for the deceleration to end.
					if ((serviceMode == Fan_Service_Mode_PWM) && !pwmOff) {
						// Target Control should be 0
						if (targetControl != 0) {
							// Jump to the first step if we came here from the fan starting branch
							setServiceMode(Fan_Service_Mode_None);
							break;
						}

						if (currentControl != 0) {
							// Here and below, we will not check timeouts while waiting,
							// since the main thread is not blocked. If there are any problems,
							// then most likely their cause is somewhere around calling the increments procedure.
							break;
						}

						//pwmOff = true;			// This is for future use
					}

					// Step 3:
					// We should only enter here if we came from previous steps,
					// but not from subsequent ones.
					if ((serviceMode != Fan_Service_Mode_DCDC) && !dcdcOff && dcdcHardwareAvailable) {
						// Here we are sure that the PWM is set to zero.
						bool immediately = ((configuration->model & Fan_Model_Connected) == 0);

						// Set DC-DC output to minimum
						setServiceMode(Fan_Service_Mode_DCDC);
						setControlTarget(0, immediately, false);
						dcdcOff = immediately;
					}

					// Step 4:
					// If we are in DC-DC mode, then wait for the deceleration to end.
					if ((serviceMode == Fan_Service_Mode_DCDC) && !dcdcOff) {
						// Target Control should be 0
						if (targetControl != 0) {
							// Jump to the first step if we came here from the fan starting branch
							setServiceMode(Fan_Service_Mode_None);
							break;
						}

						if (currentControl != 0) {
							// No timeouts checking
							break;
						}

						//dcdcOff = true;			// This is for future use
					}

					// Step 5:
					// We can only find ourselves here if we have completed
					// all the previous steps or did not need them.
					if ((configuration->hardware & Fan_Hardware_MOSFET_Available) != 0) {
						setServiceMode(Fan_Service_Mode_MOSFET);
						// We can turn off the MOSFET right here, but it's better to do this
						// through the target Control management procedures, so that there are not
						// many points of access the hardware driver (in other words, so as not to mess up).
						setControlTarget(0, true, false);
					}

					// Exit Service Mode
					setServiceMode(Fan_Service_Mode_None);
					state.status &= ~(Fan_Status_PID_Suspended | Fan_Status_PID_Action_Required);
					state.status |= Fan_Status_Switched_Off;
					fanLogicInProgress = false;
				}

			} else {

				// Fan start automation
				if (!fanRunning) {
					// Fan switching on time-distributed algorithm

					// A flag indicating that the fan stop or start (including spin-up) routine is active.
					fanLogicInProgress = true;

					// The fan is no longer considered switched off from the very first step of the starting
					// sequence. This allows the stopping branch to take over at any step if the fan
					// has to be stopped again before it is completely started.
					state.status &= ~Fan_Status_Switched_Off;

					// Step 0:
					// Checking whether the equipment is turned on
					// so as not to take unnecessary steps.
					bool mosfetOn = true;
					bool dcdcOn = true;
					bool pwmOn = true;

					if (mosfetHardwareAvailable) checkControlEqualsForMOSFET(FAN_CONTROL_TOP, mosfetOn);
					if (dcdcHardwareAvailable) checkControlEqualsForDCDC(FAN_CONTROL_TOP, dcdcOn);
					if (pwmHardwareAvailable) checkControlEqualsForPWM(FAN_CONTROL_TOP, pwmOn);


					// A small alternative way to determine that the equipment
					// is configured correctly for the current Control Mode (for future use)
					//
					// bool controlsOK = true;
					// if ((state.controlMode != Fan_Control_MOSFET) && !mosfetOn) controlsOK = false;
					// if ((state.controlMode != Fan_Control_DCDC) && !dcdcOn) controlsOK = false;
					// if ((state.controlMode != Fan_Control_PWM) && !pwmOn) controlsOK = false;

					// Step 1:
					// The fan turn on/change Control Mode command has just been received...
					if ((serviceMode == Fan_Service_Mode_None) &&
					// ...and the MOSFET available, turned off and isn't used to control the fan...
						mosfetHardwareAvailable && !mosfetOn && !mosfetControlMode) {

						// ...then turn it on.
						// But first, just in case, make sure that PWM and DC-DC are set to 0.
						safelyTurnOnMOSFET(dcdcHardwareAvailable, pwmHardwareAvailable);

						//mosfetOn = true;		// This is for future use
					}

					// Step 2:
					// If we are not yet at the stage of waiting for the 100% DC-DC output...
					if ((serviceMode != Fan_Service_Mode_DCDC) &&
						// ...and the DC-DC available, isn't set to 100% and isn't used to control the fan...
						dcdcHardwareAvailable && (state.controlMode != Fan_Control_DCDC) && !dcdcOn) {

						// ...then we must turn it SLOWLY on to 100%.
						// Because we are absolutely sure that the fan is connected,
						// otherwise we would not have exited the Fan_Control_Disabled mode.
						setServiceMode(Fan_Service_Mode_DCDC);
						setControlTarget(FAN_CONTROL_TOP, false, false);
					}

					// Step 3:
					// Let's wait for the DC-DC output to reach maximum.
					if ((serviceMode == Fan_Service_Mode_DCDC) && !dcdcOn) {

						// Target Control should be maximum
						if (targetControl != FAN_CONTROL_TOP) {
							// Jump to the first step if we came here from the stopping branch
							setServiceMode(Fan_Service_Mode_None);
							break;
						}

						if (currentControl != FAN_CONTROL_TOP) {
							// No timeouts checking
							break;
						}

						//dcdcOn = true;		// This is for future use
					}

					// Step 4:
					// If we are not yet at the stage of waiting for the 100% PWM output...
					if ((serviceMode != Fan_Service_Mode_PWM) &&
						// ...and the PWM available, isn't set to 100% and isn't used to control the fan...
						pwmHardwareAvailable && (state.controlMode != Fan_Control_PWM) && !pwmOn) {

						// ...then we must turn it on at 100%.
						// Check whether the fan supports PWM.
						// In all other cases, we will increase the PWM smoothly.
						bool immediately = ((configuration->model & Fan_Model_PWM) == 0);

						setServiceMode(Fan_Service_Mode_PWM);
						setControlTarget(FAN_CONTROL_TOP, immediately, false);
						pwmOn = immediately;
					}

					// Step 5:
					// Let's wait for the PWM output to reach maximum.
					if ((serviceMode == Fan_Service_Mode_PWM) && !pwmOn) {

						// Target Control should be maximum
						if (targetControl != FAN_CONTROL_TOP) {
							// Jump to the first step if we came here from the stopping branch
							setServiceMode(Fan_Service_Mode_None);
							break;
						}

						if (currentControl != FAN_CONTROL_TOP) {
							// No timeouts checking
							break;
						}

						//pwmOn = true;			// This is for future use
					}

					// Step 6:
					// We can only find ourselves here if we have completed
					// all the previous steps or did not need them.

					// Exit Service Mode
					setServiceMode(Fan_Service_Mode_None);
					state.status |= Fan_Status_Running;
					fanRunning = true;
					fanLogicInProgress = false;

					// Update the Targets according to the Operating Mode
					// and/or the Control Value from the sensors.
					updateControlValues();

					// Check if we need to spin-up
					if ((mosfetControlMode && (state.currentPPM < (targetPPM >> FAN_ERR_LOW_PPM_THRESHOLD_SHIFT))) ||
						(!mosfetControlMode && (state.currentPPM < (minPPM >> FAN_ERR_LOW_PPM_THRESHOLD_SHIFT)))) {
						// If we set this flag now, then additional PPM checks
						// will not be performed and spinning-up will start immediately.
						spinUpRequired = true;
					}
				}
			}

		} while (0); // "goto" replacement



		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// ERROR CHECKING SECTION
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

		// Get the time elapsed since the previous update
		uint32_t dt = HAL_GetTick();

		// A good old trick to save 4 bytes of RAM
		if (lastUpdateTick == 0) {
			lastUpdateTick = dt;
			dt = FAN_BASIC_OPERATIONS_PERIOD;
		} else {
			dt -= lastUpdateTick;
			lastUpdateTick += dt;
		}

		// Protection against division by zero in the PID calculations
		if (dt == 0) dt = 1;


		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// CHECK POWER ERRORS
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

		// Check whether the voltage should be relatively stable at the moment
		uint16_t controlDelta = (currentControl > targetControl) ? (currentControl - targetControl) : (targetControl - currentControl);
		bool controlNearTarget = (controlDelta < (FAN_CONTROL_FULL_RANGE >> FAN_ERR_CONTROL_NEAR_RANGE_SHIFT));

		// If voltmeter available...
		if (vmeterHardwareAvailable &&
			// ...and fan's logic isn't in progress...
			!fanLogicInProgress &&
			// ...and we have correct previous voltage value...
			(lastVoltage != FAN_INDEFINITE_VOLTAGE) &&
			// ...and DC-DC is not used to control the fan,
			((state.controlMode != Fan_Control_DCDC) ||
			// or the algorithm for smoothly changing the control value
			// (in this case, the voltage) has already done its job.
			controlNearTarget)) {

			// Then check Fan's power
			int32_t targetVoltage;
			switch (fanRunning ? state.controlMode : Fan_Control_Disabled) {
				case Fan_Control_PWM:

					// In PWM mode, the fan supply voltage must be at maximum.
					targetVoltage = configuration->maxVoltage;
					break;

				case Fan_Control_DCDC: {

					// We can easily calculate the expected voltage
					// because the DC-DC output voltage varies linearly
					// within its control range (and saturates beyond it).
					uint16_t control = currentControl;
					if (control < configuration->minVoltageControl) control = configuration->minVoltageControl; else
						if (control > configuration->maxVoltageControl) control = configuration->maxVoltageControl;
					targetVoltage = Fan_base::mapUInt(control, configuration->minVoltageControl, configuration->maxVoltageControl, configuration->minVoltage, configuration->maxVoltage);

				};	break;

				case Fan_Control_MOSFET:

					// For ON/OFF mode, the output voltage will be either 0 or the maximum.
					targetVoltage = (currentControl == 0) ? 0 : configuration->maxVoltage;
					break;

				case Fan_Control_Disabled:
				default:
					// When the fan is disabled...
					// ...output voltage should be around 0 if the MOSFET hardware available...
					if (configuration->hardware & Fan_Hardware_MOSFET_Available) {
						targetVoltage = 0;
					} else if (configuration->hardware & Fan_Hardware_DCDC_Available) {
						// ...or around the min DC-DC voltage if the DC-DC is present.
						targetVoltage = configuration->minVoltage;
					} else {
						// If neither ON/OFF mode nor DC-DC mode is available,
						// then the voltage will always be at maximum.
						targetVoltage = configuration->maxVoltage;
					}

					break;
			}

			// Calculate and analyze the delta between target and actual voltage.
			int32_t pwrDelta = targetVoltage - state.currentVoltage;
			int32_t deltaThreshold = configuration->maxVoltage >> FAN_ERR_PWR_ALARM_THRESHOLD_SHIFT;		// 12v / 4 = 3v
			bool alarmThresholdPassed = abs(pwrDelta) > deltaThreshold;
			deltaThreshold >>= FAN_ERR_PWR_WARNING_ADDITIONAL_SHIFT;										// 12v / 8 = 1.5v

			// If the voltage is over the threshold and does not change in the right direction...
			if (((pwrDelta > deltaThreshold) && (lastVoltage >= state.currentVoltage)) ||
				((pwrDelta < -deltaThreshold) && (lastVoltage <= state.currentVoltage))) {

				if (alarmThresholdPassed) {
					// ...check if we have to rise alarm or, if necessary, set the fan to the Failure state...
					bool alarmActivated = (state.status & Fan_Status_PWR_Alarm) != 0;
					if (countErrors(true, &fanErrorCounters, FAN_ERR_COUNTER_PWR_ALARM_SHIFT, Fan_Status_PWR_Alarm)) {
						setFailure = true;
						// This is to detect errors that disappear and recur with some periodicity.
						if (!alarmActivated) errorPressureSteps = 2;			// Not more than 3 errors per 15 mins
					}
				} else {
					// ...or just rise the warning.
					countErrors(true, &fanErrorCounters, FAN_ERR_COUNTER_PWR_WARNING_SHIFT, Fan_Status_PWR_Warning);
				}

			} else {
				// If everything is fine, then we can decrement the error counters.
				countErrors(false, &fanErrorCounters, FAN_ERR_COUNTER_PWR_ALARM_SHIFT, Fan_Status_PWR_Alarm);
				countErrors(false, &fanErrorCounters, FAN_ERR_COUNTER_PWR_WARNING_SHIFT, Fan_Status_PWR_Warning);
			}
		}


		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// CHECK PPM ERRORS AND PROCESS SPINNING-UP
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

		// If the fan is running...
		if (fanRunning &&
			// ...and has a tachometer...
			((configuration->model & Fan_Model_Tachometer) != 0) &&
			// ...and there are no problems with the tachometer.
			((state.status & Fan_Status_TACH_HW_Error) == 0)) {

			// These variables will only be used if we are not in MOSFET Control Mode.
			// But to make the compiler happy, let's initialize them anyway.
			//uint16_t minControl, maxControl, controlSupplement, controlRange;
			uint16_t minControl = 0, maxControl = 0, controlSupplement = 0, controlRange = 0;
			// We will need these parameters only if we are in either PWM or DC-DC Control Modes.
			if (!mosfetControlMode) {
				minControl = pwmControlMode ? configuration->minPWMControl : configuration->minPWRControl;
				maxControl = pwmControlMode ? FAN_CONTROL_TOP : configuration->maxVoltageControl;
				controlSupplement = minControlSupplementSteps * (pwmControlMode ? FAN_PWM_CONTROL_STEP : FAN_DCDC_CONTROL_STEP);
				controlRange = maxControl - minControl;

				// Now we must take into account the accumulated corrective supplement.
				minControl += minControlSupplementSteps * (pwmControlMode ? FAN_PWM_CONTROL_STEP : FAN_DCDC_CONTROL_STEP);
			}

			bool pid = pidEnabled();
			bool spinningUp = (state.status & Fan_Status_Spinning_Up) != 0;	// The fan is at the spinning-up step
			bool notAccelerating = state.currentPPM <= lastPPM;				// The fan is not accelerating
			bool zeroPPM = state.currentPPM == 0;								// Rotor stopped
			uint32_t lowPPMThreshold = targetPPM >> FAN_ERR_LOW_PPM_THRESHOLD_SHIFT;

			if (!spinningUp) {

				// If the fan speed is low relative to the target...
				if (state.currentPPM < lowPPMThreshold) {
					// ...and it is clear that it is not in the process of moving towards the target...
					if (notAccelerating && controlNearTarget && !spinUpRequired && ((state.status & Fan_Status_PID_Action_Required) == 0)) {

						// Is target speed in the area of minimum PPM?
						controlDelta = (minControl > currentControl) ? (minControl - currentControl) : (currentControl - minControl);
						bool controlAroundMin = (controlDelta < (FAN_CONTROL_FULL_RANGE >> FAN_ERR_CONTROL_NEAR_RANGE_SHIFT));

						if (controlAroundMin && !mosfetControlMode) {
							// Checking the stability of low-speed operation

							// Can we increase the Control Supplement so that the fan
							// is guaranteed not to stop at low speed?
							if (controlSupplement < (controlRange >> FAN_ERR_MAX_SUPPLEMENT_RANGE_SHIFT)) {
								minControlSupplementSteps++;					// Increase Supplement value
								updateControlValues();							// Update Targets
								spinUpRequired = true;							// And try to spin-up the fan
								errorPressureSteps = 1;							// Not more than 6 errors per 15 mins
							} else {
								// If the entire possible correction range is used up, then this is a fatal error!
								countErrors(true, &fanErrorCounters, FAN_ERR_COUNTER_PPM_ALARM_SHIFT, Fan_Status_PPM_Alarm, true);
								setFailure = true;
								failureSeverity = true;
								state.status |= Fan_Status_Fatal_Failure;
							}

						// If the Control Value is not in the minimum range,
						// and the fan speed is below the thresholds,
						// then we accumulate errors.
						} else if ((zeroPPM &&
							countErrors(true, &fanErrorCounters, FAN_ERR_COUNTER_PPM_ALARM_SHIFT, Fan_Status_PPM_Alarm, !mosfetControlMode)) ||
							countErrors(true, &fanErrorCounters, FAN_ERR_COUNTER_PPM_WARNING_SHIFT, Fan_Status_PPM_Warning)) {

							spinUpRequired = true;								// And try to spin-up the fan
							errorPressureSteps = 2;								// Not more than 3 errors per 15 mins
						}
					}

				} else {
					// If everything is fine, then we can decrement the error counters.
					countErrors(false, &fanErrorCounters, FAN_ERR_COUNTER_PPM_ALARM_SHIFT, Fan_Status_PPM_Alarm);
					countErrors(false, &fanErrorCounters, FAN_ERR_COUNTER_PPM_WARNING_SHIFT, Fan_Status_PPM_Warning);
				}

				// If previous checks revealed the need to spin-up the fan
				if (spinUpRequired) {

					// Can we spin-up the fan to some minimum speed (to make it quieter)?
					uint16_t spinUpControl = getSpinUpControlFromOption(configuration->options,
							pwmControlMode ? Fan_Options_Spin_PWM_Bit_Shift : Fan_Options_Spin_PWR_Bit_Shift);
					// If not, then we'll run it to the maximum!
					if ((targetControl >= spinUpControl) /*|| !notMosfetControlMode*/) spinUpControl = FAN_CONTROL_TOP;

					// Start spinning-up
					setControlTarget(spinUpControl, false, false);
					suspendPID(pid);

					state.status |= Fan_Status_Spinning_Up;
					spinningUp = true; 											// Just in case
				}

			} else {

				// If we are in the process of spinning-up and the PPM is high enough...
				if (state.currentPPM >= lowPPMThreshold) {

					// ...then exit spin-up step.
					state.status &= ~(Fan_Status_Spinning_Up | Fan_Status_Spin_Up_Failed | Fan_Status_PID_Suspended);
					spinningUp = false; 										// Just in case
					updateControlValues(); 										// Update Targets

				} else {

					// If the fan speed is insufficient, but we expect it to increase
					if (currentControl != targetControl) {

						serviceTick = HAL_GetTick();							// Update timeout start tick

					} else if (notAccelerating) {

						// But if the speed increase is not expected and we have waited enough time...
						if ((HAL_GetTick() - serviceTick) > FAN_SCAN_SPIN_UP_TIMEOUT) {

							// If this was the first attempt, we can try once again...
							if (((state.status & Fan_Status_Spin_Up_Failed) == 0) && (targetControl < FAN_CONTROL_TOP)) {

								suspendPID(pid);
								// Start spinning-up again
								setControlTarget(FAN_CONTROL_TOP, false, false);

							} else {
								// Or is the spinning-up is completely failed...
								countErrors(true, &fanErrorCounters, FAN_ERR_COUNTER_PPM_ALARM_SHIFT, Fan_Status_PPM_Alarm, true);
								setFailure = true;
								failureSeverity = true;
							}

						// A little trick to check: have we just started the fan or we already have PPM errors.
						} else if (zeroPPM && ((fanErrorCounters & FAN_ERR_COUNTER_PPM_ERRORS_MASK) != 0)) {
							// Rise alarm in case if the rotor stopped
							countErrors(true, &fanErrorCounters, FAN_ERR_COUNTER_PPM_ALARM_SHIFT, Fan_Status_PPM_Alarm, true);
						}
					}
				}
			}


			/////////////////////////////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////////////////////////////
			// PID REGULATOR
			/////////////////////////////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////////////////////////////

			// Fan speed automation (PID)
			if (pid) {

				bool pidSuspended = (state.status & Fan_Status_PID_Suspended) != 0;
				bool resumePID = false;

				// Check if the PID has been suspended and needs to be resumed
				if (pidSuspended) {
					// Check whether the PID can handle the current PPM delta
					resumePID = (!zeroPPM && pidCanHandleDelta(state.currentPPM, targetPPM, minPPM)) ||
							// Let's also add a timeout check just in case
							(!spinningUp && ((HAL_GetTick() - serviceTick) >= FAN_PID_SUSPEND_TIMEOUT));
				}

				// Check if PID available at the moment
				if ((!pidSuspended && (currentControl == targetControl)) || resumePID ||
					// Also check if it is necessary to enforce the PID, even if it is suspended.
					((state.currentPPM < targetPPM) && (currentControl > targetControl)) ||
					((state.currentPPM > targetPPM) && (currentControl < targetControl))) {

					// Reset spinning-up and PID suspended statuses
					state.status &= ~(Fan_Status_Spinning_Up | Fan_Status_Spin_Up_Failed | Fan_Status_PID_Suspended);

					// If we can switch directly from the spinning-up state
					// to control via the PID, then...
					if (spinningUp) {
						// We must first update the targets
						// that may have changed during the spinning-up.
						updateControlValues();									// Update Targets
						spinningUp = false;										// Just in case
					} else {

						// If the PID has just started
						if (pidIntegralComponent == INT32_MIN) {
							// Reset the PID parameters so that they reflect the current Control Value
							uint16_t pidStartControl = (currentControl < minControl) ? minControl :
									(currentControl > maxControl) ? maxControl : currentControl;
							pidPrevErr = 0;
							pidIntegralComponent = Fan_base::mapUInt(pidStartControl, minControl, maxControl, 0, FAN_CONTROL_TOP) * 256;
						}

						do { // "goto" replacement
							// If the fan has a significant speed increase between 99% and 100% PWM,
							// then we must take this fact into account.
							if (pwmControlMode && ((configuration->model & Fan_Model_PWM_Excessive_Full_Speed) != 0)) {
								// If the Target PPM is closer to the top point
								if (targetPPM > ((configuration->pwmPenultimatePPM + configuration->maxPPM) >> 1)) {
									// Set the Control Value to top and skip the PID operation
									setControlTarget(FAN_CONTROL_TOP, false, false);
									break; // "goto" replacement
								}

								// Set maxControl to ~99%
								maxControl -= FAN_SCAN_PWM_EXCESSIVE_FS_STEP;
							}

							// Calculate error, normalized to the full Control Range
							int32_t normalizedErr = (int32_t)(((int64_t)targetPPM - (int64_t)state.currentPPM) * FAN_CONTROL_TOP / (int32_t)(configuration->maxPPM - minPPM));

							// Calculate PID components
							bool useSlowCoefficients = (pwmControlMode ? (configuration->options >> Fan_Options_PID_Slow_PWM_Bit_Shift) : (configuration->options >> Fan_Options_PID_Slow_PWR_Bit_Shift)) & 1;
							int32_t pidProportionalComponent  = normalizedErr * (useSlowCoefficients ? FAN_PID_SLOW_PROPORTIONAL_K : FAN_PID_PROPORTIONAL_K);
							pidIntegralComponent = Fan_base::constrainUInt(normalizedErr * (useSlowCoefficients ? FAN_PID_SLOW_INTEGRAL_K : FAN_PID_INTEGRAL_K) / 1000 * (int32_t)dt + pidIntegralComponent, 0, FAN_CONTROL_TOP << 8);
							int32_t pidDerivativeComponent = (normalizedErr - pidPrevErr) * (useSlowCoefficients ? FAN_PID_SLOW_DERIVATIVE_K : FAN_PID_DERIVATIVE_K) / (int32_t)dt * 1000;
							pidPrevErr = normalizedErr;

							// Calculate new Target Control Value, scaled to the fan's control range.
							uint16_t newTargetControl = Fan_base::constrainUInt((pidProportionalComponent + pidIntegralComponent + pidDerivativeComponent) / 256, 0, FAN_CONTROL_TOP);
							newTargetControl = Fan_base::mapUInt(newTargetControl, 0, FAN_CONTROL_TOP, minControl, maxControl);

							// Set new Control Target
							setControlTarget(newTargetControl, false, false);

						} while (0); // "goto" replacement

						//  PID action done
						state.status &= ~Fan_Status_PID_Action_Required;
					}

				} else {
					// Next time we will have to reset the integral and derivative components of the PID
					pidIntegralComponent = INT32_MIN;
				}

			}
		}


		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// PROCESS ACCUMULATED ERRORS
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

		// Check the hardware errors
		if (state.status & Fan_Status_HW_Errors_Mask) {
			uint8_t bestControlMode = getBestControlMode(configuration);
			// If there are any errors, let's try to switch
			// to another Control Mode using the working equipment.
			if (bestControlMode != state.controlMode) {
				if ((setControlMode(bestControlMode) != HAL_OK) || (bestControlMode == Fan_Control_Disabled)) {
					// Fatal error!
					setFailure = true;
					failureSeverity = true;
					state.status |= Fan_Status_Fatal_Failure;
				}
			}

			if ((state.status & Fan_Status_TACH_HW_Error) != 0) {
				// We can no longer control the spinning-up,
				// so let's clear this status just in case.
				state.status &= ~Fan_Status_Spinning_Up;
			}
		}

		// Handling recurring errors.
		// They must be taken into account so that there are no
		// continuous attempts to spin-up the faulty fan.
		if (errorPressureSteps) {
			if (errorPressureCounter < FAN_ERR_PRESSURE_MAX) {
				errorPressureCounter += (errorPressureSteps * FAN_ERR_PRESSURE_STEP);
			} else {
				errorPressureCounter = FAN_ERR_PRESSURE_MAX;
			}
		} else {
			if (errorPressureCounter >= dt) {
				errorPressureCounter -= dt;
			} else {
				errorPressureCounter = 0;
			}
		}

		// If patience has run out, then switch the fan to the Failed state.
		if (errorPressureCounter >= FAN_ERR_PRESSURE_FAILURE_THRESHOLD) {
			setFailure = true;
			failureSeverity = true;
		}

		// Counting the number of failures and changing the Failed state.
		// If we are working in a Professional Mode, then it will be impossible
		// to return the fan to normal operation!
		if (setFailure) {
			if (countErrors(true, &fanErrorCounters, FAN_ERR_COUNTER_FAN_FAILED_SHIFT, Fan_Status_Failed, failureSeverity)) {
				if ((Fan_base::configFlags & Fan_Config_Flag_Professional_Mode) != 0) {
					// Here we can ignore the result, because
					// we are already in a fatal failure state.
					everythingOn100percent(mosfetHardwareAvailable, dcdcHardwareAvailable, pwmHardwareAvailable);
					state.status |= Fan_Status_Fatal_Failure;
				}
			}
		} else if (((fanErrorCounters & FAN_ERR_COUNTER_FAN_ERRORS_MASK) == 0) && ((state.status & Fan_Status_Fatal_Failure) == 0)) {
			if (countErrors(false, &fanErrorCounters, FAN_ERR_COUNTER_FAN_FAILED_SHIFT, Fan_Status_Failed)) {
				// For future use
			}
		}

		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// IT'S ALL DONE!
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

	} // The end of very-very long "if" at the very beginning of the function


	// Save the previous PPM and Voltage values
	lastVoltage = state.currentVoltage;
	lastPPM = state.currentPPM;

	return ((state.status & Fan_Status_Any_Problems_Mask) != 0);
}

// END-OF-FILE
