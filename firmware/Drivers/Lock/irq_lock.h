////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// irq_lock.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	irq_lock.h
 * @brief	Bare-metal (no FreeRTOS) lock mechanisms.
 * @note	All the locks are safe to nest: an instance counts the nesting depth
 * 			and restores the state saved by the outermost lock() call.
 */


#ifndef _IRQ_LOCK_H_
#define _IRQ_LOCK_H_


// Includes
#include "ilock.h"


/**
 * @brief	Masks ALL maskable interrupts (PRIMASK) for the duration of the section.
 * @note	Cortex-M0 has no BASEPRI, so selective priority masking (as on
 * 			M3/M4/M7) is not available here. Safe to call from either main/task
 * 			context or from an ISR.
 */
class IrqLock : public ILock {

private:

	uint32_t primask = 0;		///< PRIMASK state before the outermost lock()
	uint32_t depth = 0;			///< Nesting depth


public:

    void lock() override {
        uint32_t mask = __get_PRIMASK();
        __disable_irq();
        __DSB();
        __ISB();
        if (depth++ == 0) primask = mask;
    }

    void unlock() override {
        if ((depth > 0) && (--depth == 0) && !primask) {
            __enable_irq();
        }
    }

};


/**
 * @brief	Masks a single NVIC interrupt line instead of all interrupts.
 * @note	Use this when the shared resource is only ever touched by ONE specific ISR,
 * 			so unrelated time-critical interrupts stay enabled during the section.
 * 			Not intended for nesting.
 */
class IrqLineLock : public ILock {

private:

    IRQn_Type irq;				///< Interrupt line
    bool wasEnabled = false;	///< The line was enabled before lock()


public:

	/**
	 * @brief	Constructor.
	 * @param	irq				Interrupt line to mask.
	 */
    explicit IrqLineLock(IRQn_Type irq) : irq(irq) {}

    void lock() override {
        wasEnabled = (NVIC_GetEnableIRQ(irq) != 0);
        NVIC_DisableIRQ(irq);
        __DSB();
        __ISB();
    }

    void unlock() override {
        if (wasEnabled) {
            NVIC_EnableIRQ(irq);
        }
    }

};


/**
 * @brief	Coordinates access to a shared resource between main()/task context and
 * 			a PendSV-deferred handler.
 * @note	Only protects the main() <-> PendSV_Handler relationship - a real hardware ISR
 * 			touching the same resource directly still needs IrqLock/IrqLineLock.
 * @attention	tryEnterFromHandler() must NEVER re-arm PendSV itself while
 * 			the resource is locked. Doing so causes exception tail-chaining to loop
 * 			forever. Instead, the retry is deferred and performed by unlock(), once
 * 			it's actually safe to re-dispatch.
 */
class PendSvLock : public ILock {

private:
    volatile bool locked = false;				///< The resource is locked by main()
    volatile bool retryRequested = false;		///< The handler was rejected and must be re-dispatched
    volatile uint8_t depth = 0;					///< Nesting depth


public:
    void lock() override {
        depth++;
        locked = true;
        __DMB();
    }

    void unlock() override {
        __DMB();
        if ((depth > 0) && (--depth == 0)) {
            locked = false;

            if (retryRequested) {
                retryRequested = false;
                SCB->ICSR = SCB_ICSR_PENDSVSET_Msk;		// Re-arm PendSV if needed
            }
        }
    }

	/**
	 * @brief	Check whether the handler may access the resource.
	 * @note	Call at the top of PendSV_Handler, before touching the shared resource.
	 * @retval	true if the resource is free. false if the caller must bail out immediately
	 * 			without doing any work - the request remains logically pending
	 * 			and unlock() guarantees a retry dispatch later.
	 */
    bool tryEnterFromHandler() {
        if (locked) {
            retryRequested = true;
            return false;
        }
        return true;
    }

};


/**
 * @brief	Disables every NVIC-enabled interrupt except the allowed ones (keepMask)
 * 			for the duration of the lock.
 * @note	Reentrant - safe to nest (e.g. searchNext() calling reset() which itself
 * 			acquires the same lock). The global interrupt state (PRIMASK) isn't changed.
 */
class SelectiveIrqLock : public ILock {

private:

    uint32_t keepMask = 0;		///< Allowed-IRQ mask
    uint32_t savedMask = 0;		///< Snapshot of NVIC state
    uint8_t  depth = 0;			///< Reentrancy depth


public:

	/**
	 * @brief	Constructor.
	 * @param	keepMask		Mask of the interrupts that stay enabled (see ComputeMask()).
	 */
    explicit SelectiveIrqLock(uint32_t keepMask) : keepMask(keepMask) {}

	/**
	 * @brief	Compute the mask of the allowed interrupts.
	 * @param	irqs			Array of the allowed interrupt lines.
	 * @param	count			Number of interrupt lines.
	 * @retval	Interrupt mask.
	 */
    static uint32_t ComputeMask(const IRQn_Type * irqs, size_t count) {
        uint32_t mask = 0;
        for (size_t i = 0; i < count; i++) {
            mask |= (1UL << static_cast<uint32_t>(irqs[i]));
        }
        return mask;
    }

    void lock() override {
        uint32_t primask = __get_PRIMASK();
        __disable_irq();							// protect depth/savedMask themselves
        if (depth == 0) {
            savedMask = NVIC->ISER[0];				// remember exactly what was enabled
            NVIC->ICER[0] = 0xFFFFFFFFu;			// disable everything...
            NVIC->ISER[0] = savedMask & keepMask;	// ...except the allowed (and previously enabled) IRQs
        }
        depth++;
        __set_PRIMASK(primask);
    }

    void unlock() override {
        uint32_t primask = __get_PRIMASK();
        __disable_irq();
        if ((depth > 0) && (--depth == 0)) {
            NVIC->ICER[0] = 0xFFFFFFFFu;
            NVIC->ISER[0] = savedMask;				// restore exactly as it was
        }
        __set_PRIMASK(primask);
    }

};


#endif /* _IRQ_LOCK_H_ */

// END-OF-FILE
