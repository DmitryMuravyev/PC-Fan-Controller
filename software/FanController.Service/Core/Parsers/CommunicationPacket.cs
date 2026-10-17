////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// CommunicationPacket.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Diagnostics;


namespace FanController.Service.Core.Parsers;

//
// Types and flags:
//

/// <summary>
/// USB HID Reports
/// </summary>
public enum USB_HID_Reports : byte
{
    USB_HID_Report_System_State = 1,    // System State Report
    USB_HID_Report_System_Config = 2,    // System Configuration Report
    USB_HID_Report_Fan_Count = 3,    // Fan Count Report
    USB_HID_Report_Fan_State = 4,    // Fan State Report
    USB_HID_Report_Fan_Config = 5,    // Fan Configuration Report
    USB_HID_Report_Fan_Substitution_Matrix = 6,    // Fan Substitution Matrix Report
    USB_HID_Report_Sensor_Count = 7,    // Sensor Count Report
    USB_HID_Report_Sensor_State = 8,    // Sensor State Report
    USB_HID_Report_Sensor_Config = 9,    // Sensor Configuration Report
    USB_HID_Report_Operation_Result = 16,   // The result of executing a command from the host

    USB_HID_Report_Host_Command = 128   // A report with a command from host
}


/// <summary>
/// Commands from the host
/// </summary>
public enum Communication_Commands : byte
{
    Command_Get_System_State                = 1,    // Get System State (Report ID 1)
    Command_Get_System_Config               = 2,    // Get System Configuration (Report ID 2)
    Command_Get_Fan_Count                   = 3,    // Get Fan Count (Report ID 3)
    Command_Get_Fan_N_State                 = 4,    // Get Fan # State (Report ID 4)
    Command_Get_Fan_N_Config                = 5,    // Get Fan # Configuration (Report ID 5)
    Command_Get_Fan_Substitution_Matrix     = 6,    // Get Fan Substitution Matrix (Report ID 6)
    Command_Get_Sensor_Count                = 7,    // Get Sensor Count (Report ID 7)
    Command_Get_Sensor_N_State              = 8,    // Get Sensor # State (Report ID 8)
    Command_Get_Sensor_SN_State             = 9,    // Get Sensor ######## State (Report ID 8)
    Command_Get_Sensor_N_Config             = 10,   // Get Sensor # Configuration (Report ID 9)
    Command_Get_Sensor_SN_Config            = 11,   // Get Sensor ######## Configuration (Report ID 9)

    Command_Update_Sensor_SN_Temperature    = 16,   // Set Software Sensor ######## Temperature

    Command_Reload_Config                   = 20,   // Reload active Configuration
    Command_Set_Behavior_Config             = 21,   // Set some options, display and buzzer settings without saving them to the system configuration

    Command_Set_Fan_N_Control_Value         = 24,   // Set Fan # Control Value
    Command_Set_Fan_N_Control_Basis         = 25,   // Set Fan # Control Basis
    Command_Set_Fan_N_Control_Mode          = 26,   // Set Fan # Control Mode
    Command_Set_Fan_N_Operation_Mode        = 27,   // Set Fan # Operation Mode
    Command_Reset_Fan_N                     = 28,   // Reset Fan #
    Command_Reset_Sensor_SN                 = 29,   // Reset Sensor ########

    Command_Rescan_Fan_Hardware             = 32,   // Rescan Fan # Hardware (#6 = rescan all hardware)
    Command_Rescan_Fan_Model                = 33,   // Rescan Fan # Model (#6 = rescan all fans)
    Command_Rescan_Sensors                  = 34,   // Rescan Sensors

    Command_Add_Sensor                      = 40,   // Insert Sensor after ######## Sensor
    Command_Remove_Sensor_SN                = 41,   // Remove ######## Sensor

    Command_Set_System_Config               = 64,   // Set System Configuration (configuration page will be completed)
    Command_Set_Fan_N_Config                = 65,   // Set Fan # Configuration
    Command_Set_Fan_Substitution_Matrix     = 66,   // Set Fan Substitution Matrix
    Command_Set_Sensor_Config               = 67,   // Set Sensor ######## Configuration (SN is contained in the configuration itself)

    Command_Start_Clean_Config_Page         = 96,   // Start New Configuration Page
    Command_Finalize_Config_Page            = 97,   // Write Config OK/Version Marker and 'close' Configuration Page

    Command_Factory_Reset                   = 128,  // Erase all configuration pages and reboot
    Command_Reboot                          = 129,  // Reboot MCU
    Command_Prepare_To_Host_Shutdown        = 130   // Prepare controller to Host shutdown (reboot or power off)
}


/// <summary>
/// Communication Protocol Operation Results
/// </summary>
public enum Operation_Results : byte
{
    Response_OK                             = 0,    // OK
    Response_NACK                           = 1,    // Command Not ACKnowledged
    Response_Busy                           = 2,    // System is busy
    Response_Request_Scheduled              = 3,    // The request will be executed in the main thread (in this case, the main loop).
    Response_Request_Processing             = 4,    // The request has been previously received and is being processed.
    Response_Request_Done                   = 5,    // User Action successfully done
    Response_Request_Error                  = 6,    // Error processing User Action
    Response_Request_Unknown_Result         = 7,    // The request was processed, but the result of execution is unknown.

    Response_System_Not_Configured          = 16,   // No System Configuration available

    Response_Fan_Not_Found                  = 20,   // There is no fan with this index in the System
    Response_Fan_Not_Configured             = 21,   // No Fan Configuration available

    Response_Sensor_Not_Found               = 30,   // There is no sensor with this SN in the System
    Response_Sensor_Not_Configured          = 31,   // No Sensor Configuration available
    Response_Sensor_Already_Exists_Error    = 32,   // The sensor being added already exists

    Response_Configuration_Update_Error     = 50,   // Failed to update configuration (for future use)
    Response_Flash_Page_Already_Started     = 51,   // The previously started Flash page must be closed before starting a new one
    Response_Flash_No_Page_Started          = 52    // The new configuration page in Flash has not been erased
}


/*
//
// Prototype declaration:
//

typedef struct __attribute__((packed))
{
	uint8_t reportID;								            // USB HID Report ID
	uint8_t code;                                               // Command/Response code
	uint32_t requestID;                                         // Request ordinal number
} Communication_Packet_Header;		// 1 + 1 + 4 = 6 bytes


typedef struct __attribute__((packed))
{
	Communication_Packet_Header header;
	uint8_t data[64 - sizeof(Communication_Packet_Header)];		// Max USB HID Report size: 64 - 1 - 4 - 1 = 58
} Communication_Packet;				// 6 + 58 = 64 bytes

*/


/// <summary>
/// A USB HID report: header (report ID, code, request ID) and payload
/// </summary>
public ref struct CommunicationPacket
{
    /// <summary>
    /// Header size (the payload follows it)
    /// </summary>
    public const int HeaderSize = 6;

    // Packed structure data
    private Span<byte> _dataSpan;


    /// <summary>
    /// Wraps the raw report; it must hold the header and at least
    /// <paramref name="payloadSize"/> payload bytes.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public CommunicationPacket(Span<byte> data, int payloadSize = 0)
    {
        PackedStructureMethods.EnsureSize(data, HeaderSize + payloadSize, nameof(CommunicationPacket));
        _dataSpan = data;
    }


    /// <summary>
    /// USB HID Report ID
    /// </summary>
    public byte ReportID
    {
        get => _dataSpan[0];
        set => _dataSpan[0] = value;
    }


    /// <summary>
    /// Command/Response code
    /// </summary>
    public byte Code
    {
        get => _dataSpan[1];
        set => _dataSpan[1] = value;
    }


    /// <summary>
    /// Request ordinal number
    /// </summary>
    public UInt32 RequestID
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 2);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 2);
    }


    /// <summary>
    /// Payload. A longer value is truncated to the payload area of the report.
    /// </summary>
    public Span<byte> Data
    {
        get => _dataSpan[HeaderSize..];
        set
        {
            int payloadSize = _dataSpan.Length - HeaderSize;
            Debug.Assert(value.Length <= payloadSize, "The payload doesn't fit the report.");

            int size = Math.Min(payloadSize, value.Length);
            value[..size].CopyTo(_dataSpan.Slice(HeaderSize, size));
        }
    }

}

// END-OF-FILE
