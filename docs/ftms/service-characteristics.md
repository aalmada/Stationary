---
type: Bluetooth service specification section
title: "4 Service Characteristics"
description: "Fitness Machine Service characteristic definitions, field encodings, procedures, and transmission requirements."
tags: [bluetooth, ftms, fitness-machine-service]
status: stable
generated:
  by: docling
  at: 2026-09-06T21:49:29Z
sources:
  - id: ftms-v1-0-1
    resource: https://www.bluetooth.com/specifications/specs/fitness-machine-service-1-0-1/
    title: Fitness Machine Service Bluetooth Service Specification v1.0.1
---

<!-- markdownlint-disable MD001 MD012 MD013 MD024 MD025 -->

# 4 Service Characteristics

The following characteristics are exposed in the Fitness Machine Service.

Only one instance of each characteristic is permitted within this service.

Where a characteristic can be notified or indicated, a Client Characteristic Configuration descriptor shall be included in that characteristic as required by the Core Specification [1].

| Characteristic Name              | Requirement   | Mandatory Properties   | Optional Properties   | Security Permissions   |
|----------------------------------|---------------|------------------------|-----------------------|------------------------|
| Fitness Machine Feature          | M             | Read                   | Indicate C.7          | None                   |
| Treadmill Data                   | O             | Notify                 | N/A                   | None                   |
| Cross Trainer Data               | O             | Notify                 | N/A                   | None                   |
| Step Climber Data                | O             | Notify                 | N/A                   | None                   |
| Stair Climber Data               | O             | Notify                 | N/A                   | None                   |
| Rower Data                       | O             | Notify                 | N/A                   | None                   |
| Indoor Bike Data                 | O             | Notify                 | N/A                   | None                   |
| Training Status                  | O             | Read, Notify           | N/A                   | None                   |
| Supported Speed Range            | C.1           | Read                   | N/A                   | None                   |
| Supported Inclination Range      | C.2           | Read                   | N/A                   | None                   |
| Supported Resistance Level Range | C.3           | Read                   | N/A                   | None                   |
| Supported Power Range            | C.4           | Read                   | N/A                   | None                   |
| Supported Heart Rate Range       | C.5           | Read                   | N/A                   | None                   |


Table 4.1: Requirements for each FTMS Characteristic

| Characteristic Name           | Requirement   | Mandatory Properties   | Optional Properties   | Security Permissions   |
|-------------------------------|---------------|------------------------|-----------------------|------------------------|
| Fitness Machine Control Point | O             | Write, Indicate        | N/A                   | Encryption             |
| Fitness Machine Status        | C.6           | Notify                 | N/A                   | None                   |

- C.1: Mandatory if the Speed Target Setting feature is supported; otherwise Optional.
- C.2: Mandatory if the Inclination Target Setting feature is supported; otherwise Optional.
- C.3: Mandatory if the Resistance Target Setting feature is supported; otherwise Optional.
- C.4: Mandatory if the Power Target Setting feature is supported; otherwise Optional.
- C.5: Mandatory if the Heart Rate Target Setting feature is supported; otherwise Optional.
- C.6: Mandatory if the Fitness Machine Control Point is supported; otherwise Optional.
- C.7: The Indicate property shall be supported for the Fitness Machine Feature characteristic if the device supports bonding and the value of the Fitness Machine Feature characteristic can change over the lifetime of the device, otherwise excluded for this service.

Notes:

Properties not listed as Mandatory or Optional are excluded for this version of the service.

## 4.1 Data Record Definition

A Data Record consists of multiple training-related data recorded at the same time (or within the same sampling period) that need to be transmitted to a Client.

In the context of a treadmill, a Data Record will be sent with one or more notifications of the Treadmill Data characteristic defined in Section 4.4.

In the context of a cross trainer, a Data Record will be sent with one or more notifications of the Cross Trainer Data characteristic defined in Section 4.5.

In the context of a step climber, a Data Record will be sent with one or more notifications of the Step Climber Data characteristic defined in Section 4.6.

In the context of a stair climber, a Data Record will be sent with one or more notifications of the Stair Climber Data characteristic defined in Section 4.7.

In the context of a rower, a Data Record will be sent with one or more notifications of the Rower Data characteristic defined in Section 4.8.

In the context of an indoor bike, a Data Record will be sent with one or more notifications of the Indoor Bike Data characteristic defined in Section 4.9.

For devices that support the low energy feature of Bluetooth, if a Data Record exceeds the ATT\_MTU size, it shall be transmitted in several notifications. Refer to Section 4.18 for additional requirements on time-sensitive data.


For BR/EDR, this restriction does not exist due to a larger MTU size.

See also Appendix 1 for more information.

## 4.2 Training Session Definition

In the context of this service, a Training Session is started when the user either presses the start button on the UI of the Server or when the Start or Resume procedure of the Fitness Machine Control Point is initiated (see Section 4.16.2.8). The Training Session lasts until the user presses either the stop button on the UI of the Server or when the Stop or Pause procedure of the Fitness Machine Control Point is initiated (see Section 4.16.2.9). Refer to Section 4.10.1 for Server requirements if the Training Status characteristic is exposed and how the Server reports Training Session changes to the Client.

If the Elapsed Time feature is supported by the Server, the Elapsed Time field that may be present in a Data Record shall represent the time since the beginning of a Training Session. For display purposes, the Client may calculate the time for each training phase by calculating the difference between the Elapsed Time field value and the time at which a Fitness Machine Status characteristic has been notified (see Section 4.10).

## 4.3 Fitness Machine Feature

The Fitness Machine Feature characteristic shall be used to describe the supported features of the Server.

The Fitness Machine Feature characteristic exposes which optional features are supported by the Server implementation.

The structure of the characteristic is defined below:

LSO

MSO

Table 4.2: Structure of the Fitness Machine Feature characteristic

|             | Fitness Machine Features   | Target Setting Features   |
|-------------|----------------------------|---------------------------|
| Octet Order | LSO...MSO                  | LSO...MSO                 |
| Data type   | 32bit                      | 32bit                     |
| Size        | 4 octets                   | 4 octets                  |
| Units       | None                       | None                      |

Reserved for Future Use (RFU) bits in the Fitness Machine Feature characteristic value shall be set to 0.

### 4.3.1 Characteristic Behavior

When read or indicated, the Fitness Machine Feature characteristic returns a value containing two fields: Fitness Machine Features and Target Setting Features. Each field is a bit field that may be used by a Client to determine the supported features of the Server as defined below.

The bits of the Fitness Machine Feature characteristic may either be static for the lifetime of the device or guaranteed to be static only during a connection. Although all defined bits in this version of this specification are required to be static during the lifetime of a device, it is possible that some future bits will be defined as being static only during a connection.


When the Client Characteristic Configuration descriptor is configured for indications and the supported features of the Server have changed, the Fitness Machine Feature characteristic shall be indicated to any bonded Collectors after reconnection.

#### 4.3.1.1 Fitness Machine Features Field

When a bit is set to 1 (True) in the Fitness Machine Features field, the Server supports the associated feature. If the Server does not support the relevant feature, the associated feature bit shall be set to 0 (False), as defined in the following table:

|   Bit Number | Definition                                    |
|--------------|-----------------------------------------------|
|            0 | Average Speed Supported 0 = False 1 = True    |
|            1 | Cadence Supported 0 = False 1 = True          |
|            2 | Total Distance Supported 0 = False 1 = True   |
|            3 | Inclination Supported 0 = False 1 = True      |
|            4 | Elevation Gain Supported 0 = False 1 = True   |
|            5 | Pace Supported 0 = False 1 = True             |
|            6 | Step Count Supported 0 = False 1 = True       |
|            7 | Resistance Level Supported 0 = False 1 = True |
|            8 | Stride Count Supported 0 = False 1 = True     |


Table 4.3: Definition of the bits of the Fitness Machine Features field

| Bit Number   | Definition                                                  |
|--------------|-------------------------------------------------------------|
| 9            | Expended Energy Supported 0 = False 1 = True                |
| 10           | Heart Rate Measurement Supported 0 = False 1 = True         |
| 11           | Metabolic Equivalent Supported 0 = False 1 = True           |
| 12           | Elapsed Time Supported 0 = False 1 = True                   |
| 13           | Remaining Time Supported 0 = False 1 = True                 |
| 14           | Power Measurement Supported 0 = False 1 = True              |
| 15           | Force on Belt and Power Output Supported 0 = False 1 = True |
| 16           | User Data Retention Supported 0 = False 1 = True            |
| 17-31        | Reserved for Future Use                                     |


#### 4.3.1.2 Target Setting Features Field

When a bit is set to 1 (True) in the Target Setting Features field, the Server supports the associated control related feature. If the Server does not support the relevant control related feature, the associated feature bit shall be set to 0 (False), as defined in the following table:

|   Bit Number | Definition                                                          |
|--------------|---------------------------------------------------------------------|
|            0 | Speed Target Setting Supported 0 = False 1 = True                   |
|            1 | Inclination Target Setting Supported 0 = False 1 = True             |
|            2 | Resistance Target Setting Supported 0 = False 1 = True              |
|            3 | Power Target Setting Supported 0 = False 1 = True                   |
|            4 | Heart Rate Target Setting Supported 0 = False 1 = True              |
|            5 | Targeted Expended Energy Configuration Supported 0 = False 1 = True |
|            6 | Targeted Step Number Configuration Supported 0 = False 1 = True     |
|            7 | Targeted Stride Number Configuration Supported 0 = False 1 = True   |
|            8 | Targeted Distance Configuration Supported 0 = False 1 = True        |


Table 4.4: Definition of the bits of the Target Setting Features field

| Bit Number   | Definition                                                                         |
|--------------|------------------------------------------------------------------------------------|
| 9            | Targeted Training Time Configuration Supported 0 = False 1 = True                  |
| 10           | Targeted Time in Two Heart Rate Zones Configuration Supported 0 = False 1 = True   |
| 11           | Targeted Time in Three Heart Rate Zones Configuration Supported 0 = False 1 = True |
| 12           | Targeted Time in Five Heart Rate Zones Configuration Supported 0 = False 1 = True  |
| 13           | Indoor Bike Simulation Parameters Supported 0 = False 1 = True                     |
| 14           | Wheel Circumference Configuration Supported 0 = False 1 = True                     |
| 15           | Spin Down Control Supported 0 = False 1 = True                                     |
| 16           | Targeted Cadence Configuration Supported 0 = False 1 = True                        |
| 17-31        | Reserved for Future Use                                                            |

## 4.4 Treadmill Data Characteristics

The Treadmill Data characteristic is used to send training-related data to the Client from a treadmill (Server). Included in the characteristic value is a Flags field (for showing the presence of optional fields) and depending upon the contents of the Flags field, it may include one or more optional fields as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.4.1 Characteristic Behavior

When the Treadmill Data characteristic is configured for notification via the Client Characteristic Configuration descriptor and training-related data is available, this characteristic shall be notified. The Server should notify this characteristic at a regular interval, typically once per second while in a connection and the interval is not configurable by the Client.


For low energy, all the fields of this characteristic cannot be present simultaneously if using a default ATT\_MTU size. Refer to Sections 4.1 and 4.19 for additional requirements on the transmission of a Data Record in multiple notifications. Refer to Section 4.18 for additional requirements on time-sensitive data.

For BR/EDR, this restriction does not exist due to a larger MTU size.

#### 4.4.1.1 Flags Field

The Flags field shall be included in the Treadmill Data characteristic.

Reserved for Future Use (RFU) bits in the Flags fields shall be set to 0.

The bits of the Flags field and relationship to bits in the Fitness Machine Feature characteristic are shown in Table 4.5.

| Flags Bit Name                                                                        | When Set to 0                     | When Set to 1                         | Corresponding Fitness Machine Feature Support bit (see Section 4.3)   |
|---------------------------------------------------------------------------------------|-----------------------------------|---------------------------------------|-----------------------------------------------------------------------|
| More Data (bit 0), see Section 4.4.1.2 and 4.19.                                      | Instantaneous Speed field present | Instantaneous Speed field not present | None                                                                  |
| Average Speed Present (bit 1), see Section 4.4.1.3.                                   | Corresponding field not present   | Corresponding field present           | Average Speed Supported (bit 0)                                       |
| Total Distance Present (bit 2), see Section 4.4.1.4.                                  | Corresponding field not present   | Corresponding field present           | Total Distance Supported (bit 2)                                      |
| Inclination and Ramp Angle Setting Present (bit 3), see Sections 4.4.1.5 and 4.4.1.6. | Corresponding field not present   | Corresponding field present           | Inclination Supported (bit 3)                                         |
| Elevation Gain Present (bit 4), see Section 4.4.1.7.                                  | Corresponding fields not present  | Corresponding fields present          | Elevation Gain Supported (bit 4)                                      |
| Instantaneous Pace Present (bit 5), see Section 4.4.1.8.                              | Corresponding field not present   | Corresponding field present           | Pace Supported (bit 5)                                                |
| Average Pace Present (bit 6), see Section 4.4.1.9.                                    | Corresponding field not present   | Corresponding field present           | Pace Supported (bit 5)                                                |
| Expended Energy Present (bit 7), see Sections 4.4.1.10, 4.4.1.11 and 4.4.1.12.        | Corresponding fields not present  | Corresponding fields present          | Expended Energy Supported (bit 9)                                     |
| Heart Rate Present (bit 8), see Section 4.4.1.13.                                     | Corresponding field not present   | Corresponding field present           | Heart Rate Measurement Supported (bit 10)                             |
| Metabolic Equivalent Present (bit 9), see Section 4.4.1.14.                           | Corresponding field not present   | Corresponding field present           | Metabolic Equivalent Supported (bit 11)                               |


Table 4.5: Bit Definitions for the Treadmill Data Characteristic

| Flags Bit Name                                                                       | When Set to 0                    | When Set to 1                | Corresponding Fitness Machine Feature Support bit (see Section 4.3)   |
|--------------------------------------------------------------------------------------|----------------------------------|------------------------------|-----------------------------------------------------------------------|
| Elapsed Time Present (bit 10), see Section 4.4.1.15.                                 | Corresponding field not present  | Corresponding field present  | Elapsed Time Supported (bit 12)                                       |
| Remaining Time Present (bit 11), see Section 4.4.1.16.                               | Corresponding field not present  | Corresponding field present  | Remaining Time Supported (bit 13)                                     |
| Force on Belt and Power Output Present (bit 12), see Sections 4.4.1.17 and 4.4.1.18. | Corresponding fields not present | Corresponding fields present | Force on Belt and Power Output Supported (bit 15)                     |

#### 4.4.1.2 Instantaneous Speed Field

The Instantaneous Speed field shall be included in the treadmill-related Data Record. If the Data Record is split into several notifications of the Treadmill Data characteristic, this field shall only be included in the Treadmill Data characteristic when the More Data bit of the Flags field is set to 0. Refer to Section 4.19 for additional information related to the transmission of a Data Record.

The Instantaneous Speed field represents the instantaneous speed of the belt of the treadmill.

#### 4.4.1.3 Average Speed Field

The Average Speed field may be included in the Treadmill Data characteristic if the device supports the Average Speed feature (see Table 4.5).

The Average Speed field represents the average speed since the beginning of the training session.

#### 4.4.1.4 Total Distance Field

The Total Distance field may be included in the Treadmill Data characteristic if the device supports the Total Distance feature (see Table 4.5).

The Total Distance field represents the total distance reported by the Server since the beginning of the training session.

#### 4.4.1.5 Inclination Field

The Inclination field may be included in the Treadmill Data characteristic if the device supports the Inclination feature (see Table 4.5).

The Inclination field represents the current inclination of the Server. A positive value means that the user feels as if they are going uphill and a negative value means that the user feels as if they are going downhill.

If this field has to be present (i.e., if the Inclination and Ramp Angle Setting Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Inclination, the Server shall use the special value 0x7FFF (i.e., decimal value of 32767 in SINT16 format), which means 'Data Not Available'.


#### 4.4.1.6 Ramp Angle Setting Field

The Ramp Angle Setting field may be included in the Treadmill Data characteristic if the device supports the Inclination feature (see Table 4.5).

The Ramp Angle Setting field represents the current setting of the ramp angle of the Server.

If this field has to be present (i.e., if the Inclination and Ramp Angle Setting Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Ramp Angle Setting, the Server shall use the special value 0x7FFF (i.e., decimal value of 32767 in SINT16 format), which means 'Data Not Available'.

#### 4.4.1.7 Positive Elevation Gain and Negative Elevation Gain Field Pair

The Positive Elevation Gain and Negative Elevation Gain field pair may be included in the Treadmill Data characteristic if the device supports the Elevation Gain feature (see Table 4.5).

The Positive Elevation Gain field represents the positive elevation gain since the training session has started.

The Negative Elevation Gain field represents the negative elevation gain since the training session has started.

#### 4.4.1.8 Instantaneous Pace Field

The Instantaneous Pace field may be included in the Treadmill Data characteristic if the device supports the Pace feature (see Table 4.5).

The Instantaneous Pace field represents the instantaneous pace of a user while exercising. This value is directly related to the instantaneous speed of the treadmill but is presented with different units.

#### 4.4.1.9 Average Pace Field

The Average Pace field may be included in the Treadmill Data characteristic if the device supports the Pace feature (see Table 4.5).

The Average Pace field represents the average pace of a user since the beginning of the training session. This value is directly related to the average speed of the treadmill but is presented with different units.

#### 4.4.1.10 Total Energy Field

The Total Energy field may be included in the Treadmill Data characteristic if the device supports the Expended Energy feature (see Table 4.5).

The Total Energy field represents the total expended energy of a user since the training session has started.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Total Energy, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.4.1.11 Energy per Hour Field

The Energy per Hour field may be included in the Treadmill Data characteristic if the device supports the Expended Energy feature (see Table 4.5).


The Energy per Hour field represents the average expended energy of a user during a period of one hour.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Hour, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.4.1.12 Energy per Minute Field

The Energy per Minute field may be included in the Treadmill Data characteristic if the device supports the Expended Energy feature (see Table 4.5).

The Energy per Minute field represents the average expended energy of a user during a period of one minute.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Minute, the Server shall use the special value 0xFF (i.e., decimal value of 257 in UINT16 format), which means 'Data Not Available'.

#### 4.4.1.13 Heart Rate Field

The Heart Rate field may be included in the Treadmill Data characteristic if the device supports the Heart Rate feature (see Table 4.5).

The Heart Rate field represents the current heart rate value of the user (e.g., measured via the contact heart rate or any other means).

#### 4.4.1.14 Metabolic Equivalent Field

The Metabolic Equivalent field may be included in the Treadmill Data characteristic if the device supports the Metabolic Equivalent feature (see Table 4.5).

The Metabolic Equivalent field represents the metabolic equivalent of the user.

#### 4.4.1.15 Elapsed Time Field

The Elapsed Time field may be included in the Treadmill Data characteristic if the device supports the Elapsed Time feature (see Table 4.5).

The Elapsed Time field represents the elapsed time of a training session since the training session has started (see Section 4.2).

Refer to Sections 4.1 and 4.18 for additional requirements on the presence of this field for the case where a Data Record is sent in multiple notifications.

#### 4.4.1.16 Remaining Time Field

The Remaining Time field may be included in the Treadmill Data characteristic if the device supports the Remaining Time feature (see Table 4.5).

The Remaining Time field represents the remaining time of a training session that has been selected.

#### 4.4.1.17 Force on Belt Field

The Force on Belt field may be included in the Treadmill Data characteristic if the device supports the Force on Belt and Power Output features (see Table 4.5).


The Force on Belt field represents the force being applied to the treadmill belt by the user's steps. A positive value means that the user is accelerating the belt and a negative value means that the user is slowing down the belt.

If this field has to be present (i.e., if the Force on Belt and Power Output Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Force on Belt, the Server shall use the special value 0x7FFF (i.e., decimal value of 32767 in SINT16 format), which means 'Data Not Available'.

#### 4.4.1.18 Power Output Field

The Power Output field may be included in the Treadmill Data characteristic if the device supports the Force on Belt and Power Output features (see Table 4.5).

The Power Output field represents the power being applied to the treadmill by the user's steps. A positive value means that the user is accelerating the belt and a negative value means that the user is slowing down the belt.

If this field has to be present (i.e., if the Force on Belt and Power Output Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Power Output, the Server shall use the special value 0x7FFF (i.e., decimal value of 32767 in SINT16 format), which means 'Data Not Available'.

## 4.5 Cross Trainer Data

The Cross Trainer Data characteristic is used to send training-related data to the Client from a cross trainer (Server). Included in the characteristic value is a Flags field (for showing the presence of optional fields and movement direction), and depending upon the contents of the Flags field, it may include one or more optional fields as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.5.1 Characteristic Behavior

When the Cross Trainer Data characteristic is configured for notification via the Client Characteristic Configuration descriptor and a training-related data is available, this characteristic shall be notified. The Server should notify this characteristic at a regular interval, typically once per second while in a connection and the interval is not configurable by the Client.

For low energy, all the fields of this characteristic cannot be present simultaneously if using a default ATT\_MTU size. Refer to Sections 4.1 and 4.19 for additional requirements on the transmission of a Data Record in multiple notifications. Refer to Section 4.18 for additional requirements on time-sensitive data.

For BR/EDR, this restriction does not exist due to a larger MTU size.

#### 4.5.1.1 Flags Field

The Flags field shall be included in the Cross Trainer Data characteristic.

Reserved for Future Use (RFU) bits in the Flags fields shall be set to 0.

The bits of the Flags field and relationship to bits in the Fitness Machine Feature characteristic are shown in Table 4.6.


Table 4.6: Bit Definitions for the Cross Trainer Data Characteristic

| Flags Bit Name                                                                         | When Set to 0                     | When Set to 1                         | Corresponding Fitness Machine Feature Support bit (see Section 4.3)   |
|----------------------------------------------------------------------------------------|-----------------------------------|---------------------------------------|-----------------------------------------------------------------------|
| More Data (bit 0), see Section 4.5.1.2 and 4.19.                                       | Instantaneous Speed field present | Instantaneous Speed field not present | None                                                                  |
| Average Speed Present (bit 1), see Section 4.5.1.3.                                    | Corresponding field not present   | Corresponding field present           | Average Speed Supported (bit 0)                                       |
| Total Distance Present (bit 2), see Section 4.5.1.4.                                   | Corresponding field not present   | Corresponding field present           | Total Distance Supported (bit 2)                                      |
| Step Count Present (bit 3), see Sections 4.5.1.5 and 4.5.1.6.                          | Corresponding fields not present  | Corresponding fields present          | Step Count Supported (bit 6)                                          |
| Stride Count Present (bit 4), see Section 4.5.1.7.                                     | Corresponding field not present   | Corresponding field present           | Stride Count Supported (bit 8)                                        |
| Elevation Gain Present (bit 5), see Section 4.5.1.8.                                   | Corresponding fields not present  | Corresponding fields present          | Elevation Gain Supported (bit 4)                                      |
| Inclination and Ramp Angle Setting Present (bit 6), see Sections 4.5.1.9 and 4.5.1.10. | Corresponding field not present   | Corresponding field present           | Inclination Supported (bit 3)                                         |
| Resistance Level Present (bit 7), see Section 4.5.1.11.                                | Corresponding field not present   | Corresponding field present           | Resistance Level Supported (bit 7)                                    |
| Instantaneous Power Present (bit 8), see Section 4.5.1.12.                             | Corresponding field not present   | Corresponding field present           | Power Measurement Supported (bit 14)                                  |
| Average Power Present (bit 9), see Section 4.5.1.13.                                   | Corresponding field not present   | Corresponding field present           | Power Measurement Supported (bit 14)                                  |
| Expended Energy Present (bit 10), see Sections 4.5.1.14, 4.5.1.15 and 4.5.1.16.        | Corresponding fields not present  | Corresponding fields present          | Expended Energy Supported (bit 9)                                     |
| Heart Rate Present (bit 11), see Section 4.5.1.17.                                     | Corresponding field not present   | Corresponding field present           | Heart Rate Measurement Supported (bit 10)                             |
| Metabolic Equivalent Present (bit 12), see Section 4.5.1.18.                           | Corresponding field not present   | Corresponding field present           | Metabolic Equivalent Supported (bit 11)                               |
| Elapsed Time Present (bit 13), see Section 4.5.1.19.                                   | Corresponding field not present   | Corresponding field present           | Elapsed Time Supported (bit12)                                        |
| Remaining Time Present (bit 14), see Section 4.5.1.20.                                 | Corresponding field not present   | Corresponding field present           | Remaining Time Supported (bit 13)                                     |
| Movement Direction (bit 15)                                                            | Forward                           | Backward                              | N/A                                                                   |


#### 4.5.1.2 Instantaneous Speed Field

The Instantaneous Speed field shall be included in the cross trainer-related Data Record. If the Data Record is split into several notifications of the Cross Trainer Data characteristic, this field shall only be included in the Cross Trainer Data characteristic when the More Data bit of the Flags field is set to 0. Refer to Section 4.19 for additional information related to the transmission of a Data Record.

#### 4.5.1.3 Average Speed Field

The Average Speed field may be included in the Cross Trainer Data characteristic if the device supports the Average Speed feature (see Table 4.6).

The Average Speed field represents the average speed since the beginning of the training session.

#### 4.5.1.4 Total Distance Field

The Total Distance field may be included in the Cross Trainer Data characteristic if the device supports the Total Distance feature (see Table 4.6).

The Total Distance field represents the total distance reported by the Server since the beginning of the training session.

#### 4.5.1.5 Step per Minute Field

The Step per Minute Rate field may be included in the Cross Trainer Data characteristic if the device supports the Step Count feature (see Table 4.6).

The Step per Minute Rate field represents the average step rate of a user during a period of one minute.

If this field has to be present (i.e., if the Step Count Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Step per Minute, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.5.1.6 Average Step Rate Field

The Average Step Rate field may be included in the Cross Trainer Data characteristic if the device supports the Step Count feature (see Table 4.6).

The Average Step Rate field represents the average step rate since the beginning of the training session.

If this field has to be present (i.e., if the Step Count Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Average Step Rate, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.5.1.7 Stride Count Field

The Stride Count field may be included in the Cross Trainer Data characteristic if the device supports the Stride Count feature (see Table 4.6).

The Stride Count field represents the total number of strides since the beginning of the training session.

#### 4.5.1.8 Positive Elevation Gain and Negative Elevation Gain Field Pair

The Positive Elevation and Negative Elevation Gain field pair may be included in the Cross Trainer Data characteristic if the device supports the Elevation Gain feature (see Table 4.6).


The Positive Elevation Gain field represents the positive elevation gain since the training session has started.

The Negative Elevation Gain field represents the negative elevation gain since the training session has started.

#### 4.5.1.9 Inclination Field

The Inclination field may be included in the Cross Trainer Data characteristic if the device supports the Inclination feature (see Table 4.6).

The Inclination field represents the current inclination of the Server. A positive value means that the user feels as if they are going uphill and a negative value means that the user feels as if they are going downhill.

If this field has to be present (i.e., if the Inclination and Ramp Angle Setting Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Inclination, the Server shall use the special value 0x7FFF (i.e., decimal value of 32767 in SINT16 format), which means 'Data Not Available'.

#### 4.5.1.10 Ramp Angle Setting Field

The Ramp Angle Setting field may be included in the Cross Trainer Data characteristic if the device supports the Inclination feature (see Table 4.6).

The Ramp Angle Setting field represents the current setting of the ramp angle of the Server.

If this field has to be present (i.e., if the Inclination and Ramp Angle Setting Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Ramp Angle Setting, the Server shall use the special value 0x7FFF (i.e., decimal value of 32767 in SINT16 format), which means 'Data Not Available'.

#### 4.5.1.11 Resistance Level Field

The Resistance Level field may be included in the Cross Trainer Data characteristic if the device supports the Resistance Level feature (see Table 4.6).

The Resistance Level field represents the value of the current value of the resistance level of the Server.

#### 4.5.1.12 Instantaneous Power Field

The Instantaneous Power field may be included in the Cross Trainer Data characteristic if the device supports the Power Measurement feature (see Table 4.6).

The Instantaneous Power field represents the value of the instantaneous power measured by the Server.

#### 4.5.1.13 Average Power Field

The Average Power field may be included in the Cross Trainer Data characteristic if the device supports the Power Measurement feature (see Table 4.6).

The Average Power field represents the value of the average power measured by the Server since the beginning of the training session.


#### 4.5.1.14 Total Energy Field

The Total Energy field may be included in the Cross Trainer Data characteristic if the device supports the Expended Energy feature (see Table 4.6).

The Total Energy field represents the total expended energy of a user since the training session has started.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Total Energy, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.5.1.15 Energy per Hour Field

The Energy per Hour field may be included in the Cross Trainer Data characteristic if the device supports the Expended Energy feature (see Table 4.6).

The Energy per Hour field represents the average expended energy of a user during a period of one hour.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Hour, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.5.1.16 Energy per Minute Field

The Energy per Minute field may be included in the Cross Trainer Data characteristic if the device supports the Expended Energy feature (see Table 4.6).

The Energy per Minute field represents the average expended energy of a user during a period of one minute.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Minute, the Server shall use the special value 0xFF (i.e., decimal value of 255 in UINT16 format), which means 'Data Not Available'.

#### 4.5.1.17 Heart Rate Field

The Heart Rate field may be included in the Cross Trainer Data characteristic if the device supports the Heart Rate feature (see Table 4.6).

The Heart Rate field represents the current heart rate value of the user (e.g., measured via the contact heart rate or any other means).

#### 4.5.1.18 Metabolic Equivalent Field

The Metabolic Equivalent field may be included in the Cross Trainer Data characteristic if the device supports the Metabolic Equivalent feature (see Table 4.6).

The Metabolic Equivalent field represents the metabolic equivalent of the user.

#### 4.5.1.19 Elapsed Time Field

The Elapsed Time field may be included in the Cross Trainer Data characteristic if the device supports the Elapsed Time feature (see Table 4.6).


The Elapsed Time field represents the elapsed time of a training session since the training session has started (see Section 4.2).

Refer to Sections 4.1 and 4.18 for additional requirements on the presence of this field for the case where a Data Record is sent in multiple notifications.

#### 4.5.1.20 Remaining Time Field

The Remaining Time field may be included in the Cross Trainer Data characteristic if the device supports the Remaining Time feature (see Table 4.6).

The Remaining Time field represents the remaining time of a training session that has been selected.

## 4.6 Step Climber Data

The Step Climber Data characteristic is used to send training-related data to the Client from a step climber (Server). Included in the characteristic value is a Flags field (for showing the presence of optional fields), and depending upon the contents of the Flags field, it may include one or more optional fields as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.6.1 Characteristic Behavior

When the Step Climber Data characteristic is configured for notification via the Client Characteristic Configuration descriptor and training-related data is available, this characteristic shall be notified. The Server should notify this characteristic at a regular interval, typically once per second while in a connection and the interval is not configurable by the Client.

For low energy, all the fields of this characteristic cannot be present simultaneously if using a default ATT\_MTU size. Refer to Sections 4.1 and 4.19 for additional requirements on the transmission of a Data Record in multiple notifications. Refer to Section 4.18 for additional requirements on time-sensitive data.

For BR/EDR, this restriction does not exist due to a larger MTU size.

#### 4.6.1.1 Flags Field

The Flags field shall be included in the Step Climber Data characteristic.

Reserved for Future Use (RFU) bits in the Flags fields shall be set to 0.

The bits of the Flags field and relationship to bits in the Fitness Machine Feature characteristic are shown in Table 4.7.

| Flags Bit Name                                             | When Set to 0                        | When Set to 1                            | Corresponding Fitness Machine Feature Support bit (see Section 4.3)   |
|------------------------------------------------------------|--------------------------------------|------------------------------------------|-----------------------------------------------------------------------|
| More Data (bit 0), see Sections 4.6.1.2, 4.6.1.3 and 4.19. | Floors and Step Count fields present | Floors and Step Count fields not present | None                                                                  |
| Step per Minute present (bit 1), see Section 4.6.1.4.      | Corresponding field not present      | Corresponding field present              | Step Count Supported (bit 6)                                          |


Table 4.7: Bit Definitions for the Step Climber Data Characteristic

| Flags Bit Name                                                              | When Set to 0                    | When Set to 1                | Corresponding Fitness Machine Feature Support bit (see Section 4.3)   |
|-----------------------------------------------------------------------------|----------------------------------|------------------------------|-----------------------------------------------------------------------|
| Average Step Rate Present (bit 2), see Section 4.6.1.5.                     | Corresponding field not present  | Corresponding field present  | Step Count Supported (bit 6)                                          |
| Positive Elevation Gain Present (bit 3), see Section 4.6.1.6.               | Corresponding field not present  | Corresponding field present  | Elevation Gain Supported (bit 4)                                      |
| Expended Energy Present (bit 4), see Sections 4.6.1.7, 4.6.1.8 and 4.6.1.9. | Corresponding fields not present | Corresponding fields present | Expended Energy Supported (bit 9)                                     |
| Heart Rate Present (bit 5), see Section 4.6.1.10.                           | Corresponding field not present  | Corresponding field present  | Heart Rate Measurement Supported (bit 10)                             |
| Metabolic Equivalent Present (bit 6), see Section 4.6.1.11.                 | Corresponding field not present  | Corresponding field present  | Metabolic Equivalent Supported (bit 11)                               |
| Elapsed Time Present (bit 7), see Section 4.6.1.12.                         | Corresponding field not present  | Corresponding field present  | Elapsed Time Supported (bit 12)                                       |
| Remaining Time Present (bit 8), see Section 4.6.1.13.                       | Corresponding field not present  | Corresponding field present  | Remaining Time Supported (bit 13)                                     |

#### 4.6.1.2 Floors Field

The Floors field shall be included in the step climber-related Data Record. If the Data Record is split into several notifications of the Step Climber Data characteristic, this field shall only be included in the Step Climber Data characteristic when the More Data bit of the Flags field is set to 0. Refer to Section 4.19 for additional information related to the transmission of a Data Record.

The Floors field represents the total number of floors counted by the Server since the beginning of the training session.

#### 4.6.1.3 Step Count Field

The Step Count field shall be included in the step climber-related Data Record. If the Data Record is split into several notifications of the Step Climber Data characteristic, this field shall only be included in the Step Climber Data characteristic when the More Data bit of the Flags field is set to 0. Refer to Section 4.19 for additional information related to the transmission of a Data Record.

The Step Count field represents the total number of steps counted by the Server since the beginning of the training session.

#### 4.6.1.4 Step per Minute Field

The Step per Minute Rate field may be included in the Step Climber Data characteristic if the device supports the Step Count feature (see Table 4.7).

The Step per Minute Rate field represents the average step rate of a user during a period of one minute.


#### 4.6.1.5 Average Step Rate Field

The Average Step Rate field may be included in the Step Climber Data characteristic if the device supports the Step Count feature (see Table 4.7).

The Average Step Rate field represents the average step rate since the beginning of the training session.

#### 4.6.1.6 Positive Elevation Gain Field

The Positive Elevation field may be included in the Step Climber Data characteristic if the device supports the Elevation Gain feature (see Table 4.7).

The Positive Elevation Gain field represents the positive elevation gain since the beginning of the training session.

#### 4.6.1.7 Total Energy Field

The Total Energy field may be included in the Step Climber Data characteristic if the device supports the Expended Energy feature (see Table 4.7).

The Total Energy field represents the total expended energy of a user since the training session has started.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Total Energy, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.6.1.8 Energy per Hour Field

The Energy per Hour field may be included in the Step Climber Data characteristic if the device supports the Expended Energy feature (see Table 4.7).

The Energy per Hour field represents the average expended energy of a user during a period of one hour.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Hour, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.6.1.9 Energy per Minute Field

The Energy per Minute field may be included in the Step Climber Data characteristic if the device supports the Expended Energy feature (see Table 4.7).

The Energy per Minute field represents the average expended energy of a user during a period of one minute.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Minute, the Server shall use the special value 0xFF (i.e., decimal value of 255 in UINT16 format), which means 'Data Not Available'.

#### 4.6.1.10 Heart Rate Field

The Heart Rate field may be included in the Step Climber Data characteristic if the device supports the Heart Rate feature (see Table 4.7).


The Heart Rate field represents the current heart rate value of the user (e.g., measured via the contact heart rate or any other means).

#### 4.6.1.11 Metabolic Equivalent Field

The Metabolic Equivalent field may be included in the Step Climber Data characteristic if the device supports the Metabolic Equivalent feature (see Table 4.7).

The Metabolic Equivalent field represents the metabolic equivalent of the user.

#### 4.6.1.12 Elapsed Time Field

The Elapsed Time field may be included in the Step Climber Data characteristic if the device supports the Elapsed Time feature (see Table 4.7).

The Elapsed Time field represents the elapsed time of a training session since the training session has started (See Section 4.2).

Refer to Sections 4.1 and 4.18 for additional requirements on the presence of this field for the case where a Data Record is sent in multiple notifications.

#### 4.6.1.13 Remaining Time Field

The Remaining Time field may be included in the Step Climber Data characteristic if the device supports the Remaining Time feature (see Table 4.7).

The Remaining Time field represents the remaining time of a selected training session.

## 4.7 Stair Climber Data

The Stair Climber Data characteristic is used to send training-related data to the Client from a stair climber (Server). Included in the characteristic value is a Flags field (for showing the presence of optional fields), and depending upon the contents of the Flags field, it may include one or more optional fields as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.7.1 Characteristic Behavior

When the Stair Climber Data characteristic is configured for notification via the Client Characteristic Configuration descriptor and training-related data is available, this characteristic shall be notified. The Server should notify this characteristic at a regular interval, typically once per second while in a connection and the interval is not configurable by the Client.

For low energy, all the fields of this characteristic cannot be present simultaneously if using a default ATT\_MTU size. Refer to Sections 4.1 and 4.19 for additional requirements on the transmission of a Data Record in multiple notifications. Refer to Section 4.18 for additional requirements on time-sensitive data.

For BR/EDR, this restriction does not exist due to a larger MTU size.

#### 4.7.1.1 Flags Field

The Flags field shall be included in the Stair Climber Data characteristic.

Reserved for Future Use (RFU) bits in the Flags fields shall be set to 0.


The bits of the Flags field and relationship to bits in the Fitness Machine Feature characteristic are shown in Table 4.8.

Table 4.8: Bit Definitions for the Stair Climber Data Characteristic

| Flags Bit Name                                                              | When Set to 0                    | When Set to 1                | Corresponding Fitness Machine Feature Support bit (see Section 4.3)   |
|-----------------------------------------------------------------------------|----------------------------------|------------------------------|-----------------------------------------------------------------------|
| More Data (bit 0), see Section 4.7.1.2 and 4.19.                            | Floors field present             | Floors field not present     | None                                                                  |
| Step per Minute present (bit 1), see Section 4.7.1.3.                       | Corresponding field not present  | Corresponding field present  | Step Count Supported (bit 6)                                          |
| Average Step Rate Present (bit 2), see Section 4.7.1.4.                     | Corresponding field not present  | Corresponding field present  | Step Count Supported (bit 6)                                          |
| Positive Elevation Gain Present (bit 3), see Section 4.7.1.5.               | Corresponding field not present  | Corresponding field present  | Elevation Gain Supported (bit 4)                                      |
| Stride Count Present (bit 4), see Section 4.7.1.6.                          | Corresponding field not present  | Corresponding field present  | Stride Count Supported (bit 8)                                        |
| Expended Energy Present (bit 5), see Sections 4.7.1.7, 4.7.1.8 and 4.7.1.9. | Corresponding fields not present | Corresponding fields present | Expended Energy Supported (bit 9)                                     |
| Heart Rate Present (bit 6), see Section 4.7.1.10.                           | Corresponding field not present  | Corresponding field present  | Heart Rate Measurement Supported (bit 10)                             |
| Metabolic Equivalent Present (bit 7), see Section 4.7.1.11.                 | Corresponding field not present  | Corresponding field present  | Metabolic Equivalent Supported (bit 11)                               |
| Elapsed Time Present (bit 8), see Section 4.7.1.12.                         | Corresponding field not present  | Corresponding field present  | Elapsed Time Supported (bit 12)                                       |
| Remaining Time Present (bit 9), see Section 4.7.1.13.                       | Corresponding field not present  | Corresponding field present  | Remaining Time Supported (bit 13)                                     |

#### 4.7.1.2 Floors Field

The Floors field shall be included in the stair climber-related Data Record. If the Data Record is split into several notifications of the Stair Climber Data, this field shall only be included in the Stair Climber Data characteristic when the More Data bit of the Flags field is set to 0. Refer to Section 4.19 for additional information related to the transmission of a Data Record.

The Floors field represents the total number of floors counted by the Server since the beginning of the training session.

#### 4.7.1.3 Step per Minute Rate Field

The Step per Minute Rate field may be included in the Stair Climber Data characteristic if the device supports the Step Count feature (see Table 4.8).


The Step per Minute Rate field represents the average step rate of a user during a period of one minute.

#### 4.7.1.4 Average Step Rate Field

The Average Step Rate field may be included in the Stair Climber Data characteristic if the device supports the Step Count feature (see Table 4.8).

The Average Step Rate field represents the average step rate since the beginning of the training session.

#### 4.7.1.5 Positive Elevation Gain Field

The Positive Elevation Gain field may be included in the Stair Climber Data characteristic if the device supports the Elevation Gain feature (see Table 4.8).

The Positive Elevation Gain field represents the positive elevation gain since the beginning of the training session.

#### 4.7.1.6 Stride Count Field

The Stride Count field may be included in the Stair Climber Data characteristic if the device supports the Stride Count feature (see Table 4.8).

The Stride Count field represents the total number of strides since the beginning of the training session.

#### 4.7.1.7 Total Energy Field

The Total Energy field may be included in the Stair Climber Data characteristic if the device supports the Expended Energy feature (see Table 4.8).

The Total Energy field represents the total expended energy of a user since the training session has started.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Total Energy, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.7.1.8 Energy per Hour Field

The Energy per Hour field may be included in the Stair Climber Data characteristic if the device supports the Expended Energy feature (see Table 4.8).

The Energy per Hour field represents the average expended energy of a user during a period of one hour.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Hour, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.7.1.9 Energy per Minute Field

The Energy per Minute field may be included in the Stair Climber Data characteristic if the device supports the Expended Energy feature (see Table 4.8).

The Energy per Minute field represents the average expended energy of a user during a period of one minute.


If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Minute, the Server shall use the special value 0xFF (i.e., decimal value of 255 in UINT16 format), which means 'Data Not Available'.

#### 4.7.1.10 Heart Rate Field

The Heart Rate field may be included in the Stair Climber Data characteristic if the device supports the Heart Rate feature (see Table 4.8).

The Heart Rate field represents the current heart rate value of the user (e.g., measured via the contact heart rate or any other means).

#### 4.7.1.11 Metabolic Equivalent Field

The Metabolic Equivalent field may be included in the Stair Climber Data characteristic if the device supports the Metabolic Equivalent feature (see Table 4.8).

The Metabolic Equivalent field represents the metabolic equivalent of the user.

#### 4.7.1.12 Elapsed Time Field

The Elapsed Time field may be included in the Stair Climber Data characteristic if the device supports the Elapsed Time feature (see Table 4.8).

The Elapsed Time field represents the elapsed time of a training session since the training session has started (see Section 4.2).

Refer to Sections 4.1 and 4.18 for additional requirements on the presence of this field for the case where a Data Record is sent in multiple notifications.

#### 4.7.1.13 Remaining Time Field

The Remaining Time field may be included in the Stair Climber Data characteristic if the device supports the Remaining Time feature (see Table 4.8).

The Remaining Time field represents the remaining time of a training session that has been selected.

## 4.8 Rower Data

The Rower Data characteristic is used to send training-related data to the Client from a rower (Server). Included in the characteristic value is a Flags field (for showing the presence of optional), and depending upon the contents of the Flags field, it may include one or more optional fields as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.8.1 Characteristic Behavior

When the Rower Data characteristic is configured for notification via the Client Characteristic Configuration descriptor and training-related data is available, this characteristic shall be notified. The Server should notify this characteristic at a regular interval, typically once per second while in a connection and the interval is not configurable by the Client.

For low energy, all the fields of this characteristic cannot be present simultaneously if using a default ATT\_MTU size. Refer to Sections 4.1 and 4.19 for additional requirements on the transmission of a Data Record in multiple notifications. Refer to Section 4.18 for additional requirements on time-sensitive data.

For BR/EDR, this restriction does not exist due to a larger MTU size.


#### 4.8.1.1 Flags Field

The Flags field shall be included in the Rower Data characteristic.

Reserved for Future Use (RFU) bits in the Flags fields shall be set to 0.

The bits of the Flags field and relationship to bits in the Fitness Machine Feature characteristic are shown in Table 4.9.

Table 4.9: Bit Definitions for the Rower Data Characteristic

| Flags Bit Name                                                                 | When Set to 0                              | When Set to 1                                   | Corresponding Fitness Machine Feature Support bit (see Section 4.3)   |
|--------------------------------------------------------------------------------|--------------------------------------------|-------------------------------------------------|-----------------------------------------------------------------------|
| More Data (bit 0), see Sections 4.8.1.2, 4.8.1.3 and 4.19.                     | Stroke Rate and Stroke Count field present | Stroke Rate and Stroke Count fields not present | None                                                                  |
| Average Stroke Rate present (bit 1), see Section 4.8.1.4.                      | Corresponding field not present            | Corresponding field present                     | Cadence Supported (bit 1)                                             |
| Total Distance Present (bit 2), see Section 4.8.1.5.                           | Corresponding field not present            | Corresponding field present                     | Total Distance Supported (bit 2)                                      |
| Instantaneous Pace Present (bit 3), see Section 4.8.1.6.                       | Corresponding field not present            | Corresponding field present                     | Pace Supported (bit 5)                                                |
| Average Pace Present (bit 4), see Section 4.8.1.7.                             | Corresponding field not present            | Corresponding field present                     | Pace Supported (bit 5)                                                |
| Instantaneous Power Present (bit 5), see Section 4.8.1.8.                      | Corresponding field not present            | Corresponding field present                     | Power Measurement Supported (bit 14)                                  |
| Average Power Present (bit 6), see Section 4.8.1.9.                            | Corresponding field not present            | Corresponding field present                     | Power Measurement Supported (bit 14)                                  |
| Resistance Level (bit 7), see Section 4.8.1.10.                                | Corresponding field not present            | Corresponding field present                     | Resistance Level Supported (bit 7)                                    |
| Expended Energy Present (bit 8), see Sections 4.8.1.11, 4.8.1.12 and 4.8.1.13. | Corresponding fields not present           | Corresponding fields present                    | Expended Energy Supported (bit 9)                                     |
| Heart Rate Present (bit 9), see Section 4.8.1.14.                              | Corresponding field not present            | Corresponding field present                     | Heart Rate Measurement Supported (bit 10)                             |
| Metabolic Equivalent Present (bit 10), see Section 4.8.1.15.                   | Corresponding field not present            | Corresponding field present                     | Metabolic Equivalent Supported (bit 11)                               |
| Elapsed Time Present (bit 11), see Section 4.8.1.16.                           | Corresponding field not present            | Corresponding field present                     | Elapsed Time Supported (bit 12)                                       |
| Remaining Time Present (bit 12), see Section 4.8.1.17.                         | Corresponding field not present            | Corresponding field present                     | Remaining Time Supported (bit 13)                                     |


#### 4.8.1.2 Stroke Rate Field

The Stroke Rate field shall be included in the rower-related Data Record. If the Data Record is split into several notifications of the Rower Data characteristic, this field shall only be included in the Rower Data characteristic when the More Data bit of the Flags field is set to 0. Refer to Section 4.19 for additional information related to the transmission of a Data Record.

The Stroke Rate field represents the instantaneous stroke rate measured by the Server.

#### 4.8.1.3 Stroke Count Field

The Stroke Count field shall be included in the rower-related Data Record. If the Data Record is split into several notifications of the Rower Data characteristic, this field shall only be included in the Rower Data characteristic when the More Data bit of the Flags field is set to 0. Refer to Section 4.19 for additional information related to the transmission of a Data Record.

The Stroke Count field represents the total number of strokes since the beginning of the training session.

#### 4.8.1.4 Average Stroke Rate Field

The Average Stroke Rate field may be included in the Rower Data characteristic if the device supports the Cadence feature (see Table 4.9).

The Average Stroke Rate field represents the average speed since the beginning of the training session.

#### 4.8.1.5 Total Distance Field

The Total Distance field may be included in the Rower Data characteristic if the device supports the Total Distance feature (see Table 4.9).

The Total Distance field represents the total distance reported by the Server since the beginning of the training session.

#### 4.8.1.6 Instantaneous Pace Field

The Instantaneous Pace field may be included in the Rower Data characteristic if the device supports the Pace feature (see Table 4.9).

The Instantaneous Pace field represents the value of the pace (time per 500 meters) of the user while exercising.

#### 4.8.1.7 Average Pace Field

The Average Pace field may be included in the Rower Data characteristic if the device supports the Pace feature (see Table 4.9).

The Average Pace field represents the value of the average pace (time per 500 meters) since the beginning of the training session.

#### 4.8.1.8 Instantaneous Power Field

The Instantaneous Power field may be included in the Rower Data characteristic if the device supports the Power Measurement feature (see Table 4.9).

The Instantaneous Power field represents the value of the instantaneous power measured by the Server.


#### 4.8.1.9 Average Power Field

The Average Power field may be included in the Rower Data characteristic if the device supports the Power Measurement feature (see Table 4.9).

The Average Power field represents the value of the average power measured by the Server since the beginning of the training session.

#### 4.8.1.10 Resistance Level Field

The Resistance Level field may be included in the Rower Data characteristic if the device supports the Resistance Level feature (see Table 4.9).

The Resistance Level field represents the value of the current value of the resistance level of the Server.

#### 4.8.1.11 Total Energy Field

The Total Energy field may be included in the Rower Data characteristic if the device supports the Expended Energy feature (see Table 4.9).

The Total Energy field represents the total expended energy of a user since the training session has started.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Total Energy, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.8.1.12 Energy per Hour Field

The Energy per Hour field may be included in the Rower Data characteristic if the device supports the Expended Energy feature (see Table 4.9).

The Energy per Hour field represents the average expended energy of a user during a period of one hour.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Hour, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.8.1.13 Energy per Minute Field

The Energy per Minute field may be included in the Rower Data characteristic if the device supports the Expended Energy feature (see Table 4.9).

The Energy per Minute field represents the average expended energy of a user during a period of one minute.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Minute, the Server shall use the special value 0xFF (i.e., decimal value of 255 in UINT16 format), which means 'Data Not Available'.

#### 4.8.1.14 Heart Rate Field

The Heart Rate field may be included in the Rower Data characteristic if the device supports the Heart Rate feature (see Table 4.9).


The Heart Rate field represents the current heart rate value of the user (e.g., measured via the contact heart rate or any other means).

#### 4.8.1.15 Metabolic Equivalent Field

The Metabolic Equivalent field may be included in the Rower Data characteristic if the device supports the Metabolic Equivalent feature (see Table 4.9).

The Metabolic Equivalent field represents the metabolic equivalent of the user.

#### 4.8.1.16 Elapsed Time Field

The Elapsed Time field may be included in the Rower Data characteristic if the device supports the Elapsed Time feature (see Table 4.9).

The Elapsed Time field represents the elapsed time of a training session since the training session has started (see Section 4.2).

Refer to Sections 4.1 and 4.18 for additional requirements on the presence of this field for the case where a Data Record is sent in multiple notifications.

#### 4.8.1.17 Remaining Time Field

The Remaining Time field may be included in the Rower Data characteristic if the device supports the Remaining Time feature (see Table 4.9).

The Remaining Time field represents the remaining time of a selected training session.

## 4.9 Indoor Bike Data

The Indoor Bike Data characteristic is used to send training-related data to the Client from an indoor bike (Server). Included in the characteristic value is a Flags field (for showing the presence of optional fields), and depending upon the contents of the Flags field, it may include one or more optional fields as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.9.1 Characteristic Behavior

When the Indoor Bike Data characteristic is configured for notification via the Client Characteristic Configuration descriptor and training-related data is available, this characteristic shall be notified. The Server should notify this characteristic at a regular interval, typically once per second while in a connection and the interval is not configurable by the Client.

For low energy, all the fields of this characteristic cannot be present simultaneously if using a default ATT\_MTU size. Refer to Sections 4.1 and 4.19 for additional requirements on the transmission of a Data Record in multiple notifications. Refer to Section 4.18 for additional requirements on time-sensitive data.

For BR/EDR, this restriction does not exist due to a larger MTU size.

#### 4.9.1.1 Flags Field

The Flags field shall be included in the Indoor Bike Data characteristic.

Reserved for Future Use (RFU) bits in the Flags fields shall be set to 0.


The bits of the Flags field and relationship to bits in the Fitness Machine Feature characteristic are shown in Table 4.10.

Table 4.10: Bit Definitions for the Indoor Bike Data Characteristic

| Flags Bit Name                                                                 | When Set to 0                     | When Set to 1                          | Corresponding Fitness Machine Feature Support bit (see Section 4.3)   |
|--------------------------------------------------------------------------------|-----------------------------------|----------------------------------------|-----------------------------------------------------------------------|
| More Data (bit 0), see Sections 4.9.1.2 and 4.19.                              | Instantaneous Speed field present | Instantaneous Speed fields not present | None                                                                  |
| Average Speed present (bit 1), see Section 4.9.1.3.                            | Corresponding field not present   | Corresponding field present            | Average Speed Supported (bit 0)                                       |
| Instantaneous Cadence (bit 2), see Section 4.9.1.4.                            | Corresponding field not present   | Corresponding field present            | Cadence Supported (bit 1)                                             |
| Average Cadence present (bit 3), see Section 4.9.1.5.                          | Corresponding field not present   | Corresponding field present            | Cadence Supported (bit 1)                                             |
| Total Distance Present (bit 4), see Section 4.9.1.6.                           | Corresponding field not present   | Corresponding field present            | Total Distance Supported (bit 2)                                      |
| Resistance Level Present (bit 5), see Section 4.9.1.7.                         | Corresponding field not present   | Corresponding field present            | Resistance Level Supported (bit 7)                                    |
| Instantaneous Power Present (bit 6), see Section 4.9.1.8.                      | Corresponding field not present   | Corresponding field present            | Power Measurement Supported (bit 14)                                  |
| Average Power Present (bit 7), see Section 4.9.1.9.                            | Corresponding field not present   | Corresponding field present            | Power Measurement Supported (bit 14)                                  |
| Expended Energy Present (bit 8), see Sections 4.9.1.10, 4.9.1.11 and 4.9.1.12. | Corresponding fields not present  | Corresponding fields present           | Expended Energy Supported (bit 9)                                     |
| Heart Rate Present (bit 9, see Section 4.9.1.13.                               | Corresponding field not present   | Corresponding field present            | Heart Rate Measurement Supported (bit 10)                             |
| Metabolic Equivalent Present (bit 10), see Section 4.9.1.14.                   | Corresponding field not present   | Corresponding field present            | Metabolic Equivalent Supported (bit 11)                               |
| Elapsed Time Present (bit 11), see Section 4.9.1.15.                           | Corresponding field not present   | Corresponding field present            | Elapsed Time Supported (bit 12)                                       |
| Remaining Time Present (bit 12), see Section 4.9.1.16.                         | Corresponding field not present   | Corresponding field present            | Remaining Time Supported (bit 13)                                     |

#### 4.9.1.2 Instantaneous Speed Field

The Instantaneous Speed field shall be included in the indoor bike-related Data Record. If the Data Record is split into several notifications of the Indoor Bike Data, this field shall only be included in the Indoor Bike Data characteristic when the More Data bit of the Flags field is set to 0. Refer to Section 4.19 for additional information related to the transmission of a Data Record.


The Instantaneous Speed field represents the instantaneous speed of the user.

#### 4.9.1.3 Average Speed Field

The Average Speed field may be included in the Indoor Bike Data characteristic if the device supports the Average Speed feature (see Table 4.10).

The Average Speed field represents the average speed since the beginning of the training session.

#### 4.9.1.4 Instantaneous Cadence Field

The Instantaneous Cadence field may be included in the Indoor Bike Data characteristic if the device supports the Cadence feature (see Table 4.10).

The Instantaneous Cadence field represents the instantaneous cadence of the user.

#### 4.9.1.5 Average Cadence Field

The Average Cadence field may be included in the Indoor Bike Data characteristic if the device supports the Cadence feature (see Table 4.10).

The Average Speed field represents the average cadence since the beginning of the training session.

#### 4.9.1.6 Total Distance Field

The Total Distance field may be included in the Indoor Bike Data characteristic if the device supports the Total Distance feature (see Table 4.10).

The Total Distance field represents the total distance reported by the Server since the beginning of the training session.

#### 4.9.1.7 Resistance Level

The Resistance Level field may be included in the Indoor Bike Data characteristic if the device supports the Resistance Level feature (see Table 4.10).

The Resistance Level field represents the value of the current value of the resistance level of the Server.

#### 4.9.1.8 Instantaneous Power

The Instantaneous Power field may be included in the Indoor Bike Data characteristic if the device supports the Power Measurement feature (see Table 4.10).

The Instantaneous Power field represents the value of the instantaneous power measured by the Server.

#### 4.9.1.9 Average Power

The Average Power field may be included in the Indoor Bike Data characteristic if the device supports the Power Measurement feature (see Table 4.10).

The Average Power field represents the value of the average power measured by the Server since the beginning of the training session.

#### 4.9.1.10 Total Energy Field

The Total Energy field may be included in the Indoor Bike Data characteristic if the device supports the Expended Energy feature (see Table 4.10).


The Total Energy field represents the total expended energy of a user since the training session has started.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Total Energy, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.9.1.11 Energy per Hour Field

The Energy per Hour field may be included in the Indoor Bike Data characteristic if the device supports the Expended Energy feature (see Table 4.10).

The Energy per Hour field represents the average expended energy of a user during a period of one hour.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Hour, the Server shall use the special value 0xFFFF (i.e., decimal value of 65535 in UINT16 format), which means 'Data Not Available'.

#### 4.9.1.12 Energy per Minute Field

The Energy per Minute field may be included in the Indoor Bike Data characteristic if the device supports the Expended Energy feature (see Table 4.10).

The Energy per Minute field represents the average expended energy of a user during a period of one minute.

If this field has to be present (i.e., if the Expended Energy Present bit of the Flags field is set to 1) but the Server does not support the calculation of the Energy per Minute, the Server shall use the special value 0xFF (i.e., decimal value of 255 in UINT16 format), which means 'Data Not Available'.

#### 4.9.1.13 Heart Rate Field

The Heart Rate field may be included in the Indoor Bike Data characteristic if the device supports the Heart Rate feature (see Table 4.10).

The Heart Rate field represents the current heart rate value of the user (e.g., measured via the contact heart rate or any other means).

#### 4.9.1.14 Metabolic Equivalent Field

The Metabolic Equivalent field may be included in the Indoor Bike Data characteristic if the device supports the Metabolic Equivalent feature (see Table 4.10).

The Metabolic Equivalent field represents the metabolic equivalent of the user.

#### 4.9.1.15 Elapsed Time Field

The Elapsed Time field may be included in the Indoor Bike Data characteristic if the device supports the Elapsed Time feature (see Table 4.10).

The Elapsed Time field represents the elapsed time of a training session since the training session has started (See Section 4.2).

Refer to Sections 4.1 and 4.18 for additional requirements on the presence of this field for the case where a Data Record is sent in multiple notifications.


#### 4.9.1.16 Remaining Time Field

The Remaining Time field may be included in the Indoor Bike Data characteristic if the device supports the Remaining Time feature (see Table 4.10).

The Remaining Time field represents the remaining time of a selected training session.

## 4.10 Training Status

The Training Status characteristic shall be used by the Server to send the training status information to the Client. Included in the characteristic value is a Flags field (for showing the presence of optional fields), a Training Status field, and depending upon the contents of the Flags field, also a Training Status String.

The structure of the characteristic is defined below:

LSO

MSO

Table 4.11: Structure of the Training Status Characteristic

|             | Flags   | Training Status   | Training Status String (if present)   |
|-------------|---------|-------------------|---------------------------------------|
| Octet Order | N/A     | N/A               | LSO…MSO                               |
| Data type   | 8bit    | 8bit              | UTF8 String                           |
| Size        | 1 octet | 1 octet           | Variable                              |
| Units       | None    | None              | None                                  |

### 4.10.1 Characteristic Behavior

When the Training Status characteristic is configured for notification via the Client Characteristic Configuration descriptor and a new training status is available (e.g., when there is a transition in the training program), this characteristic shall be notified.

When read, the Training Status characteristic returns a value that is used by a Client to determine the current training status of the Server.

The Training Status characteristic contains time-sensitive data, thus the requirements for time-sensitive data and data storage defined in Section 4.18 apply.

#### 4.10.1.1 Flags Field

The Flags field shall be included in the Training Status characteristic.

Reserved for Future Use (RFU) bits in the Flags fields shall be set to 0.

The bits of the Flags field and their function are shown in Table 4.12.


Table 4.12: Bit Definitions for the Training State Characteristic

| Bit   | Definition                                         |
|-------|----------------------------------------------------|
| 0     | Training Status String present: 0 = False 1 = True |
| 1     | Extended String present: 0 = False 1 = True        |
| 2-7   | Reserved for Future Use                            |

For low energy, the Training Status characteristic may exceed the current MTU size (i.e., if the Training Status String field exceeds the negotiated MTU size minus 5 octets). If the characteristic size exceeds the current MTU size, the Server shall set the Extended String bit of the Flags field to 1 to inform the Client that additional characters are available and should be read via the appropriate GATT procedure (e.g., GATT Read Long procedure). The Server should not update the value more than once in a ten seconds period if the Extended String present bit is set to 1 in order to give enough time to the Client to read this value. Only the first (ATT\_MTU-3) octets of the characteristic value can be included in a notification, but the entire string may be read by using the GATT Read Long sub-procedure.

For BR/EDR, this restriction does not exist due to a larger MTU size.

#### 4.10.1.2 Training Status Field

The Training Status field shall be included in the Training Status characteristic.

The Training Status field represents the current training state while a user is exercising. The values of the Training Status field are defined in Table 4.13.

| Value   | Definition              |
|---------|-------------------------|
| 0x00    | Other                   |
| 0x01    | Idle                    |
| 0x02    | Warming Up              |
| 0x03    | Low Intensity Interval  |
| 0x04    | High Intensity Interval |
| 0x05    | Recovery Interval       |
| 0x06    | Isometric               |
| 0x07    | Heart Rate Control      |
| 0x08    | Fitness Test            |


Table 4.13: Training Status Field Definition

| Value     | Definition                                                                               |
|-----------|------------------------------------------------------------------------------------------|
| 0x09      | Speed Outside of Control Region - Low (increase speed to return to controllable region)  |
| 0x0A      | Speed Outside of Control Region - High (decrease speed to return to controllable region) |
| 0x0B      | Cool Down                                                                                |
| 0x0C      | Watt Control                                                                             |
| 0x0D      | Manual Mode (Quick Start)                                                                |
| 0x0E      | Pre-Workout                                                                              |
| 0x0F      | Post-Workout                                                                             |
| 0x10-0xFF | Reserved for Future Use                                                                  |

#### 4.10.1.3 Training Status String Field

The Training Status String field may be included in the Training Status characteristic.

The Training Status String field is a string-based field that can be used to give more specific information related to the training status.

## 4.11 Supported Speed Range

The Supported Speed Range characteristic shall be exposed by the Server if the Speed Target Setting feature is supported.

The Supported Speed Range characteristic is used to send the supported speed range as well as the minimum speed increment supported by the Server. Included in the characteristic value are a Minimum Speed field, a Maximum Speed field, and a Minimum Increment field as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.11.1 Characteristic Behavior

When read, the Supported Speed Range characteristic returns a value that is used by a Client to determine the valid range that can be used in order to control the speed of the Server.

## 4.12 Supported Inclination Range

The Supported Inclination Range characteristic shall be exposed by the Server if the Inclination Target Setting feature is supported.

The Supported Inclination Range characteristic is used to send the supported inclination range as well as the minimum inclination increment supported by the Server. Included in the characteristic value are a Minimum Inclination field, a Maximum Inclination field, and a Minimum Increment field as defined on the Bluetooth SIG Assigned Numbers webpage [2].


### 4.12.1 Characteristic Behavior

When read, the Supported Inclination Range characteristic returns a value that is used by a Client to determine the valid range that can be used in order to control the inclination of the Server.

## 4.13 Supported Resistance Level Range

The Supported Resistance Level Range characteristic shall be exposed by the Server if the Resistance Control Target Setting feature is supported.

The Supported Resistance Level Range characteristic is used to send the supported resistance level range as well as the minimum resistance increment supported by the Server. Included in the characteristic value are a Minimum Resistance Level field, a Maximum Resistance Level field, and a Minimum Increment field as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.13.1 Characteristic Behavior

When read, the Supported Resistance Level Range characteristic returns a value that is used by a Client to determine the valid range that can be used in order to control the resistance level of the Server.

## 4.14 Supported Power Range

The Supported Power Range characteristic shall be exposed by the Server if the Power Target Setting feature is supported.

The Supported Power Range characteristic is used to send the supported power range as well as the minimum power increment supported by the Server. Included in the characteristic value are a Minimum Power field, a Maximum Power field, and a Minimum Increment field as defined on the Bluetooth SIG Assigned Numbers webpage [2]. Note that the Minimum Power field and the Maximum Power field represent the extreme values supported by the Server and are not related to, for example, the current speed of the Server.

### 4.14.1 Characteristic Behavior

When read, the Supported Power Range characteristic returns a value that is used by a Client to determine the valid range that can be used in order to control the power reference of the Server.

## 4.15 Supported Heart Rate Range

The Supported Heart Rate Range characteristic shall be exposed by the Server if the Heart Rate Target Setting feature is supported.

The Supported Heart Rate Range characteristic is used to send the supported Heart Rate range as well as the minimum Heart Rate increment supported by the Server. Included in the characteristic value are a Minimum Heart Rate field, a Maximum Heart Rate field, and a Minimum Increment field as defined on the Bluetooth SIG Assigned Numbers webpage [2].

### 4.15.1 Characteristic Behavior

When read, the Supported Heart Rate Range characteristic returns a value that is used by a Client to determine the valid range that can be used in order to set the heart rate target for a given training session.


## 4.16 Fitness Machine Control Point

The Server may expose the Fitness Machine Control Point.

The Fitness Machine Control Point characteristic is used to request a specific function to be executed on the Server.

The format of the Fitness Machine Control Point characteristic is defined in Table 4.14.

LSO

MSO

Table 4.14: Fitness Machine Control Point Characteristic Format

|            | Op Code (see Table 4.15)   | Parameter (see Table 4.15)   |
|------------|----------------------------|------------------------------|
| Byte Order | N/A                        | LSO...MSO                    |
| Data type  | UINT8                      | Variable                     |
| Size       | 1 octet                    | 0 to 18 octets               |
| Units      | None                       | None                         |

The Op Codes, Parameters, and requirements for the Fitness Machine Control Point are defined in Section 4.16.1.

### 4.16.1 Fitness Machine Control Point Procedure Requirements

A Client shall use the GATT Write Characteristic Value sub-procedure to initiate a procedure defined in Table 4.15.

The Op Codes, Parameters, and their requirements are defined in Table 4.15.

| Op Code Value   | Requirement   | Definition                  | Parameter Value   | Description                                                                                                                                                                      |
|-----------------|---------------|-----------------------------|-------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 0x00            | M             | Request Control             | N/A               | Initiates the procedure to request the control of a fitness machine. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.             |
| 0x01            | M             | Reset, see Section 4.16.2.2 | N/A               | Initiates the procedure to reset the controllable settings of a fitness machine. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value. |


| Op Code Value   | Requirement   | Definition                                        | Parameter Value                                                    | Description                                                                                                                                                                                                                                         |
|-----------------|---------------|---------------------------------------------------|--------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 0x02            | C.1           | Set Target Speed, see Section 4.16.2.3            | Target Speed, UINT16, in km/h with a resolution of 0.01 km/h       | Initiate the procedure to set the target speed of the Server. The desired target speed is sent as parameters to this op code. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.                       |
| 0x03            | C.2           | Set Target Inclination, see Section 4.16.2.4      | Target Inclination, SINT16, in Percent with a resolution of 0.1 %  | Initiate the procedure to set the target inclination of the Server. The desired target inclination is sent as parameters to this op code. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.           |
| 0x04            | C.3           | Set Target Resistance Level, see Section 4.16.2.5 | Target Resistance Level, UINT8, unitless with a resolution of 0.1. | Initiate the procedure to set the target resistance level of the Server. The desired target resistance level is sent as parameters to this op code. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value. |
| 0x05            | C.4           | Set Target Power, see Section 4.16.2.6            | Target Power, SINT16, in Watt with a resolution of 1 W.            | Initiate the procedure to set the target power of the Server. The desired target power is sent as parameters to this op code. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.                       |
| 0x06            | C.5           | Set Target Heart Rate, see Section 4.16.2.7       | Target Heart Rate, UINT8, in BPM with a resolution of 1 BPM.       | Initiate the procedure to set the target heart rate of the Server. The desired target heart rate is sent as parameters to this op code. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.             |


| Op Code Value   | Requirement   | Definition                                            | Parameter Value                                                               | Description                                                                                                                                                                 |
|-----------------|---------------|-------------------------------------------------------|-------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 0x07            | M             | Start or Resume, see Section 4.16.2.8                 | N/A                                                                           | Initiate the procedure to start or resume a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value. |
| 0x08            | M             | Stop or Pause, see Section 4.16.2.9                   | Control Information, see Section 4.16.2.9.                                    | Initiate the procedure to stop or pause a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.   |
| 0x09            | C.6           | Set Targeted Expended Energy, see Section 4.16.2.10   | Targeted Expended Energy, UINT16, in Calories with a resolution of 1 Calorie. | Set the targeted expended energy for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.      |
| 0x0A            | C.7           | Set Targeted Number of Steps, see Section 4.16.2.11   | Targeted Number of Steps, UINT16, in Steps with a resolution of 1 Step.       | Set the targeted number of steps for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.      |
| 0x0B            | C.8           | Set Targeted Number of Strides, see Section 4.16.2.12 | Targeted Number of Strides, UINT16, in Stride with a resolution of 1 Stride.  | Set the targeted number of strides for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.    |
| 0x0C            | C.9           | Set Targeted Distance, see Section 4.16.2.13          | Targeted Distance, UINT24, in Meters with a resolution of 1 Meter.            | Set the targeted distance for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.             |


| Op Code Value   | Requirement   | Definition                                                         | Parameter Value                                                               | Description                                                                                                                                                                           |
|-----------------|---------------|--------------------------------------------------------------------|-------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 0x0D            | C.10          | Set Targeted Training Time, see Section 4.16.2.14                  | Targeted Training Time, UINT16, in Seconds with a resolution of 1 Second.     | Set the targeted training time for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.                  |
| 0x0E            | C.11          | Set Targeted Time in Two Heart Rate Zones, see Section 4.16.2.15   | Targeted Time Array, see Section 4.16.2.15.                                   | Set the targeted time in two heart rate zones for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.   |
| 0x0F            | C.12          | Set Targeted Time in Three Heart Rate Zones, see Section 4.16.2.16 | Targeted Time Array, see Section 4.16.2.16.                                   | Set the targeted time in three heart rate zones for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value. |
| 0x10            | C.13          | Set Targeted Time in Five Heart Rate Zones, see Section 4.16.2.17  | Targeted Time Array, see Section 4.16.2.17.                                   | Set the targeted time in five heart rate zones for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.  |
| 0x11            | C.14          | Set Indoor Bike Simulation Parameters, see Section 4.16.2.18       | Simulation Parameter Array, see Section 4.16.2.18                             | Set the simulation parameters for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.                   |
| 0x12            | O             | Set Wheel Circumference, see Section 4.16.2.19                     | Wheel Circumference, UINT16, in Millimeters with resolution of 0.1 Millimeter | Set the wheel circumference for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.                     |


Table 4.15: Fitness Machine Control Point Procedure Requirements

| Op Code Value   | Requirement   | Definition                               | Parameter Value                                                          | Description                                                                                                                                                    |
|-----------------|---------------|------------------------------------------|--------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 0x13            | O             | Spin Down Control, see Section 4.16.2.20 | Control Parameter, see Section 4.16.2.20                                 | Control the spin down procedure of a Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value.                   |
| 0x14            | C.15          | Set Targeted Cadence                     | Targeted Cadence, UINT16, in 1/minute with a resolution of 0.5 1/minute. | Set the targeted cadence for a training session on the Server. The response to this control point is Op Code 0x80 followed by the appropriate Parameter Value. |
| 0x15-0x7F       | N/A           | Reserved for Future Use                  | N/A                                                                      | N/A                                                                                                                                                            |
| 0x80            | M             | Response Code, see Section 4.16.2.22     | See Section 4.16.2.22                                                    | Used to identify the response to this Control Point.                                                                                                           |
| 0x81-0xFF       | N/A           | Reserved for Future Use                  | N/A                                                                      | N/A                                                                                                                                                            |

- C.1: Mandatory to support if the Speed Target Setting feature is supported; otherwise Excluded.
- C.2: Mandatory to support if the Inclination Target Setting feature is supported; otherwise Excluded.
- C.3: Mandatory to support if the Resistance Target Setting feature is supported; otherwise Excluded.
- C.4: Mandatory to support if the Power Target Setting feature is supported; otherwise Excluded.
- C.5: Mandatory to support if the Heart Rate Target Setting feature is supported; otherwise Excluded.
- C.6: Mandatory to support if the Targeted Energy Expended Configuration feature is supported; otherwise Excluded.
- C.7: Mandatory to support if the Targeted Number of Steps Configuration feature is supported; otherwise Excluded.
- C.8: Mandatory to support if the Targeted Number of Strides Configuration feature is supported; otherwise Excluded.
- C.9: Mandatory to support if the Targeted Distance Configuration feature is supported; otherwise Excluded.
- C.10: Mandatory to support if the Targeted Training Time Configuration feature is supported; otherwise Excluded.
- C.11: Mandatory to support if the Targeted Time in Two Heart Rate Zones Configuration feature is supported; otherwise Excluded.
- C.12: Mandatory to support if the Targeted Time in Three Heart Rate Zones Configuration feature is supported; otherwise Excluded.
- C.13: Mandatory to support if the Targeted Time in Five Heart Rate Zones Configuration feature is supported; otherwise Excluded.


- C.14: Mandatory to support if the Indoor Bike Simulation Parameters feature is supported; otherwise Excluded.
- C.15: Mandatory to support if the Set Targeted Cadence feature is supported; otherwise Excluded.

### 4.16.2 Fitness Machine Control Point Behavioral Description

The Fitness Machine Control Point is used by a Client to control certain behaviors of the Server. Procedures are triggered by a Write to this characteristic value that includes an Op Code specifying the operation (see Table 4.15), which may be followed by a Parameter that is valid within the context of that Op Code.

Each procedure defined in Sections 4.16.2.2 to 4.16.2.20 requires control permission from the Server. The Request Control procedure defined in Section 4.16.2.21 is used to request the control of the Server.

#### 4.16.2.1 Request Control Procedure

When the Request Control Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall allow the Client to perform any supported control procedures (see Sections 4.16.2.2 to 4.16.2.20).

The response shall be indicated when the Reset Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

The control permission remains valid until the connection is terminated, the notification of the Fitness Machine Status is sent with the value set to Control Permission Lost (see Section 4.17), or the Reset procedure (see Section 4.16.2.2) is initiated by the Client.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.2 Reset Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Reset Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall set the control parameters to their respective default values (e.g., target speed set to 0, inclination set to 0). In addition, if the Fitness Machine supports the Remaining Time and Elapsed Time features, it shall set the time-related fields to 0. The Training Status characteristic value shall also be set to Idle (0x01).

The response shall be indicated when the Reset Procedure is completed using the Response Code Op Code, the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.3 Set Target Speed Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.


When the Set Target Speed Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall set the Target Speed to the value sent as a Parameter.

The response shall be indicated when the Set Target Speed Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.4 Set Target Inclination Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Target Inclination Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall set the target inclination to the value sent as a Parameter. A positive value means that the user will feel as if they are going uphill and a negative value means that the user will feel as if they are going downhill.

The response shall be indicated when the Set Target Inclination Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.5 Set Target Resistance Level Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Target Resistance Level Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall set the target resistance level to the value sent as a Parameter.

The response shall be indicated when the Set Target Resistance Level Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.6 Set Target Power Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Target Power Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall set the target power to the value sent as a Parameter.


The response shall be indicated when the Set Target Power Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.7 Set Target Heart Rate Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Target Heart Rate Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall set the target heart rate to the value sent as a Parameter.

The response shall be indicated when the Set Target Heart Rate Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.8 Start or Resume Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Start or Resume Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall initiate the start procedure of the Fitness Machine.

If the Fitness Machine supports the Remaining Time and Elapsed Time features, the Fitness Machine shall update the related Remaining Time and Elapsed Time fields at a regular interval (e.g., every second). In order to set the time-related fields to zero, the Reset procedure defined in Section 4.16.2.2 shall be used.

The response shall be indicated when the Start or Resume Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.9 Stop or Pause Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Stop or Pause Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall initiate the stop or pause procedure of the Fitness Machine depending on the Control Information Parameter value. The format of the Control Information Parameter value is UINT8, and the supported values are defined in the table below:


Table 4.16: Control Information Parameter Value for Stop or Pause Procedure

| Value     | Control Information     |
|-----------|-------------------------|
| 0x00      | Reserved for Future Use |
| 0x01      | Stop                    |
| 0x02      | Pause                   |
| 0x03-0xFF | Reserved for Future Use |

If the Fitness Machine supports the Remaining Time and Elapsed Time features, the Fitness Machine shall stop updating the related Remaining Time and Elapsed Time fields. In order to set the time-related fields to zero, the Reset procedure defined in Section 4.16.2.2 shall be used.

The response shall be indicated when the Stop or Pause Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.10 Set Targeted Expended Energy Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Targeted Expended Energy Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the value sent as a Parameter as the new targeted expended energy.

The response shall be indicated when the Set Targeted Expended Energy Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.11 Set Targeted Number of Steps Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Targeted Number of Steps Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the value sent as a Parameter as the new targeted number of steps.

The response shall be indicated when the Set Targeted Number of Steps Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.


If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.12 Set Targeted Number of Strides Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Targeted Number of Strides Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the value sent as a Parameter as the new targeted number of strides.

The response shall be indicated when the Set Targeted Number of Strides Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.13 Set Targeted Distance Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Targeted Distance Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the value sent as a Parameter as the new targeted distance.

The response shall be indicated when the Set Targeted Distance Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.14 Set Targeted Training Time Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Targeted Training Time Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the value sent as a Parameter as the new targeted training time.

The response shall be indicated when the Set Targeted Training Time Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.


#### 4.16.2.15 Set Targeted Time in Two Heart Rate Zones Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Targeted Training Time in Two Heart Rate Zones Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the values sent as a Parameter as the new targeted time in each heart rate zone. The format of the Targeted Time Array Parameter is described below:

LSO

MSO

Table 4.17: Targeted Time Array Parameter Format for Set Targeted Training Time in Two Heart Rate Zones Procedure

|            | Targeted Time in Fat Burn Zone   | Targeted Time in Fitness Zone   |
|------------|----------------------------------|---------------------------------|
| Byte Order | LSO...MSO                        | LSO...MSO                       |
| Data type  | UINT16                           | UINT16                          |
| Size       | 2 octet                          | 2 octets                        |
| Units      | Second                           | Second                          |

The response shall be indicated when the Set Targeted Time in Two Heart Rate Zones Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.16 Set Targeted Time in Three Zone Heart Rate Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Targeted Training Time in Three Heart Rate Zones Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the values sent as a Parameter as the new targeted time in each heart rate zone. The format of the Targeted Time Array Parameter is described below:


LSO

MSO

Table 4.18: Targeted Time Array Parameter Format for Set Targeted Training Time in Three Heart Rate Zones Procedure

|            | Targeted Time in Light Zone   | Targeted Time in Moderate Zone   | Targeted Time in Hard Zone   |
|------------|-------------------------------|----------------------------------|------------------------------|
| Byte Order | LSO...MSO                     | LSO...MSO                        | LSO...MSO                    |
| Data type  | UINT16                        | UINT16                           | UINT16                       |
| Size       | 2 octet                       | 2 octets                         | 2 octets                     |
| Units      | Second                        | Second                           | Second                       |

The response shall be indicated when the Set Targeted Time in Three Heart Rate Zones Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.17 Set Targeted Time in Five Zone Heart Rate Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Targeted Training Time in Five Heart Rate Zones Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the values sent as a Parameter as the new targeted time in each heart rate zone. The format of the Targeted Time Array Parameter is described below:

LSO

## MSO

Table 4.19: Targeted Time Array Parameter Format for Set Targeted Training Time in Five Heart Rate Zones Procedure

|            | Targeted Time in Very Light Zone   | Targeted Time in Light Zone   | Targeted Time in Moderate Zone   | Targeted Time in Hard Zone   | Targeted Time in Maximum Zone   |
|------------|------------------------------------|-------------------------------|----------------------------------|------------------------------|---------------------------------|
| Byte Order | LSO...MSO                          | LSO...MSO                     | LSO...MSO                        | LSO...MSO                    | LSO...MSO                       |
| Data type  | UINT16                             | UINT16                        | UINT16                           | UINT16                       | UINT16                          |
| Size       | 2 octet                            | 2 octets                      | 2 octets                         | 2 octets                     | 2 octets                        |
| Units      | Second                             | Second                        | Second                           | Second                       | Second                          |

The response shall be indicated when the Set Targeted Time in Five Heart Rate Zones Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.


If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.18 Set Indoor Bike Simulation Parameters Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Indoor Bike Simulation Parameter Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the parameters values sent as a Parameter as the new simulation parameters. The format of the Simulation Parameter Array is described below:

LSO

MSO

Table 4.20: Simulation Parameter Array Format for Set Indoor Bike Simulation Parameters Procedure

|            | Wind Speed              | Grade      | Crr (Coefficient of Rolling Resistance)   | Cw (Wind Resistance Coefficient)   |
|------------|-------------------------|------------|-------------------------------------------|------------------------------------|
| Byte Order | LSO...MSO               | LSO...MSO  | LSO...MSO                                 | LSO...MSO                          |
| Data type  | SINT16                  | SINT16     | UINT8                                     | UINT8                              |
| Size       | 2 octet                 | 2 octets   | 1 octets                                  | 1 octets                           |
| Units      | Meters Per Second (mps) | Percentage | Unitless                                  | Unitless                           |
| Resolution | 0.001                   | 0.01       | 0.0001                                    | 0.01                               |

The response shall be indicated when the Set Indoor Bike Simulation Mode Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.19 Set Wheel Circumference Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Set Wheel Circumference Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the parameter value sent as a Parameter as the new wheel circumference.

The response shall be indicated when the Set Indoor Bike Simulation Mode Procedure is completed using the Response Code Op Code  and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.


#### 4.16.2.20 Spin Down Control Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.

When the Spin Down Control Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the value sent as a Parameter to initiate the appropriate spin down control. The format of the Control Parameter is UINT8 and the values are described below:

Table 4.21: Control Parameter Definition for Spin Down Control Procedure

| Control Parameter Value   | Definition              |
|---------------------------|-------------------------|
| 0x00                      | Reserved for Future Use |
| 0x01                      | Start                   |
| 0x02                      | Ignore                  |
| 0x03 - 0xFF               | Reserved for Future Use |

Refer to Appendix 3 for examples related to the Spin Down Procedure.

The response shall be indicated when the Spin Down Control Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in a success condition, the Response Parameter shall include a data structure that includes the Target Speed Low and the Target Speed High fields as defined in Table 4.22.

LSO

## MSO

Table 4.22: Response Parameter when the Spin Down Procedure succeeds

|            | Target Speed Low                    | Target Speed High                   |
|------------|-------------------------------------|-------------------------------------|
| Byte Order | LSO...MSO                           | LSO...MSO                           |
| Data type  | UINT16                              | UINT16                              |
| Size       | 2 octet                             | 2 octets                            |
| Units      | km/h with a resolution of 0.01 km/h | km/h with a resolution of 0.01 km/h |

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

Refer to Appendix 3 for additional information related to this procedure.

#### 4.16.2.21 Set Targeted Cadence Procedure

This procedure requires control permission in order to be executed. Refer to Section 4.16.2.1 for more information on the Request Control procedure.


When the Set Targeted Cadence Op Code is written to the Fitness Machine Control Point and the Result Code is 'Success', the Server shall use the value sent as a Parameter as the new targeted cadence.

The response shall be indicated when the Set Targeted Distance Procedure is completed using the Response Code Op Code and the Request Op Code , along with the appropriate Result Code as defined in Section 4.16.2.22.

If the operation results in an error condition where the Fitness Machine Control Point cannot be indicated (e.g., the Client Characteristic Configuration descriptor is not configured for indication or if a procedure is already in progress), see Section 4.16.3 for details on handling this condition.

#### 4.16.2.22 Procedure Complete

When any of the procedures described in Sections 4.16.2.2 to 4.16.2.17 have been executed by the Server or if the procedure generated an error as defined below in this section, the Server shall indicate the Fitness Machine Control Point characteristic to the Client. The format of the indication is defined in Table 4.23.

LSO

MSO

|            | Response Code Op Code (0x80)   | Parameter Value   | Parameter Value   | Parameter Value                 |
|------------|--------------------------------|-------------------|-------------------|---------------------------------|
|            |                                | Request Op Code   | Result Code       | Response Parameter (if present) |
| Byte Order | N/A                            | N/A               | N/A               | LSO…MSO                         |
| Data type  | UINT8                          | UINT8             | UINT8             | See Table 4.24                  |
| Size       | 1 octet                        | 1 octet           | 1 octet           | 0 to 17 octets                  |

Table 4.23: Fitness Machine Control Point characteristic - Parameter Value Format of the Response Indication

The Response Code field shall be set to 0x80.

The Request Op Code field shall be set to the value of the Op Code representing the requested procedure.

Table 4.24 defines the Result Code for the Fitness Machine Control Point.


Table 4.24: Fitness Machine Control Point characteristic - Result Codes

| Result Code   | Definition              | Request Op Code                                                                                                      | Response Parameter   |
|---------------|-------------------------|----------------------------------------------------------------------------------------------------------------------|----------------------|
| 0x00          | Reserved for Future Use | N/A                                                                                                                  | N/A                  |
| 0x01          | Success                 | All Op Codes defined in Table 4.15 except Spin Down Op Code (0x13).                                                  | None                 |
| 0x01          | Success                 | Spin Down Op Code (0x13).                                                                                            | See Table 4.22       |
| 0x02          | Op Code not supported   | All Op Codes defined in Table 4.15 as reserved for future use, or all Op Codes that are not supported by the Server. | None                 |
| 0x03          | Invalid Parameter       | All Op Codes defined in Table 4.15.                                                                                  | None                 |
| 0x04          | Operation Failed        | All Op Codes defined in Table 4.15.                                                                                  | None                 |
| 0x05          | Control Not Permitted   | All Op Codes defined in Table 4.15.                                                                                  | None                 |
| 0x06-0xFF     | Reserved for Future Use | N/A                                                                                                                  | N/A                  |

If an Op Code is written to the Fitness Machine Control Point that results in a successful operation, the Server shall indicate the Fitness Machine Control Point with the Response Code Op Code, the Request Op Code, and the Result Code set to ' Success '.

If an Op Code is written to the Fitness Machine Control Point and the Server does not permit the control to that particular Client, the Server shall respond with the Result Code set to ' Control Not Permitted '.

Depending on the context of the Server, if an Op Code is written to the Fitness Machine Control Point that contradicts a previously triggered operation (e.g., the Client sets the targeted speed while targeted distance was set or the spin down procedure was ongoing), then previously triggered operation should be aborted and the new procedure should be taken into account by the Server.

If the Start or Resume Op Code is written to the Fitness Machine Control Point that results in an error condition (e.g., the fitness machine has already been started), the Server, after sending a Write Response, shall indicate the Fitness Machine Control Point with the Response Code Op Code, the Request Op Code, and the Result Code set to ' Operation Failed '.

If the Stop or Pause Op Code is written to the Fitness Machine Control Point that results in an error condition (e.g., the fitness machine has already been stopped), the Server, after sending a Write Response, shall indicate the Fitness Machine Control Point with the Response Code Op Code, the Request Op Code, and the Result Code set to ' Operation Failed '.


If an Op Code is written to the Fitness Machine Control Point characteristic that is unsupported by the Server (e.g., an Op Code that is Reserved for Future Use), the Server, after sending a Write Response, shall indicate the Fitness Machine Control Point with a Response Code Op Code, the Request Op Code, and Result Code set to ' Op Code Not Supported '.

If a Parameter is written to the Fitness Machine Control Point characteristic that is invalid (e.g., the Client writes the Set Target Speed Op Code with a Parameter that is improperly formatted or that is outside the range of the supported values), the Server, after sending a Write Response, shall indicate the Fitness Machine Control Point with a Response Code Op Code, the Request Op Code, and Result Code set to ' Invalid Parameter '.

If the operation results in an error condition that cannot be reported to the Client using the Fitness Machine Control Point (e.g., the Fitness Machine Control Point cannot be indicated), see Section 4.16.3 for details on handling this condition.

### 4.16.3 General Error Handling Procedures

Other than error handling procedures that are specific to certain Op Codes, the following apply:

If an Op Code is written to the Fitness Machine Control Point characteristic while the Server is performing a previously triggered Fitness Machine Control Point operation (i.e., resulting from invalid Client behavior), the Server shall return an error response with the Attribute Protocol error code set to ' Procedure Already In Progress ' as defined in CSS Part B, Section 1.2 [3]. See Appendix 2 for an example on how the Server handles this situation.

If an Op Code is written to the Fitness Machine Control Point characteristic and the Client Characteristic Configuration descriptor of the Fitness Machine Control Point is not configured for indications, the Server shall return an error response with the Attribute Protocol error code set to ' Client Characteristic Configuration Descriptor Improperly Configured ' as defined in CSS Part B, Section 1.2 [3].

### 4.16.4 Procedure Timeout

In the context of the Fitness Machine Control Point characteristic, a procedure is started when a write to the Fitness Machine Control Point characteristic is successfully completed (i.e., the Server sends a Write Response). When a procedure is complete, the Server shall indicate the Fitness Machine Control Point with the Op Code set to ' Response Code '.

In the context of the Fitness Machine Control Point characteristic, a procedure is not considered started and not queued in the Server when a write to the Fitness Machine Control Point results in an error response with an Attribute Protocol error code.

## 4.17 Fitness Machine Status

If the Server supports the Fitness Machine Control Point (see Section 4.16), the Fitness Machine Status characteristic shall be exposed by the Server. Otherwise, supporting the Fitness Machine Status characteristic is optional.

The Fitness Machine Status characteristic is used to send the status of the Server. Included in the characteristic value is a Fitness Machine Status field, and depending on the value of the Fitness Machine Status Field, a Parameter field may also be included.


The structure of the characteristic is defined below:

Table 4.255: Structure of the Fitness Machine Status characteristic

|             | Op Code   | Parameter   |
|-------------|-----------|-------------|
| Octet Order | N/A       | LSO...MSO   |
| Data type   | 8bit      | Variable    |
| Size        | 1 octet   | Variable    |
| Units       | None      | None        |

The Fitness Machine Status Op Code and the Parameter format, if required, are defined in Table 4.26.

| Op Code   | Definition                                     | Parameter                                                                    |
|-----------|------------------------------------------------|------------------------------------------------------------------------------|
| 0x00      | Reserved for Future Use                        | N/A                                                                          |
| 0x01      | Reset                                          | N/A                                                                          |
| 0x02      | Fitness Machine Stopped or Paused by the User  | Control Information, see Table 4.16.                                         |
| 0x03      | Fitness Machine Stopped by Safety Key          | N/A                                                                          |
| 0x04      | Fitness Machine Started or Resumed by the User | N/A                                                                          |
| 0x05      | Target Speed Changed                           | New Target Value (UINT16 in kilometer per hour with a resolution 0.01 km/h)  |
| 0x06      | Target Incline Changed                         | New Target Value (SINT16 in % with a resolution of 0.1 %)                    |
| 0x07      | Target Resistance Level Changed                | New Target Value (SINT16, unitless with a resolution of 0.1)                 |
| 0x08      | Target Power Changed                           | New Target Power (SINT16, in Watt with a resolution of 1)                    |
| 0x09      | Target Heart Rate Changed                      | New Target Heart Rate (UINT8, in BPM with a resolution of 1)                 |
| 0x0A      | Targeted Expended Energy Changed               | New Targeted Expended Energy (UINT16, in Calories with a resolution of 1)    |
| 0x0B      | Targeted Number of Steps Changed               | New Targeted Number of Steps Value (UINT16, in Steps with a resolution of 1) |
| 0x0C      | Targeted Number of Strides Changed             | New Targeted Number of Strides (UINT16, in Stride with a resolution of 1)    |


Table 4.26: Fitness Machine Status values and Parameter format

| Op Code     | Definition                                      | Parameter                                                                          |
|-------------|-------------------------------------------------|------------------------------------------------------------------------------------|
| 0x0D        | Targeted Distance Changed                       | New Targeted Distance (UINT24, in Meters with a resolution of 1)                   |
| 0x0E        | Targeted Training Time Changed                  | New Targeted Training Time (UINT16, in Seconds with a resolution of 1)             |
| 0x0F        | Targeted Time in Two Heart Rate Zones Changed   | New Targeted Time Array, see Section 4.16.2.15.                                    |
| 0x10        | Targeted Time in Three Heart Rate Zones Changed | New Targeted Time Array, see Section 4.16.2.16.                                    |
| 0x11        | Targeted Time in Five Heart Rate Zones Changed  | New Targeted Time Array, see Section 4.16.2.17.                                    |
| 0x12        | Indoor Bike Simulation Parameters Changed       | New Indoor Bike Simulation Parameters, see Section 4.16.2.18.                      |
| 0x13        | Wheel Circumference Changed                     | New Wheel Circumference (UINT16, in Millimeters with resolution of 0.1 Millimeter) |
| 0x14        | Spin Down Status                                | Spin Down Status Value, see Table 4.27                                             |
| 0x15        | Targeted Cadence Changed                        | New Targeted Cadence (UINT16, in 1/minute with a resolution of 0.5)                |
| 0x16 - 0xFE | Reserved for Future Use                         | N/A                                                                                |
| 0xFF        | Control Permission Lost                         | N/A                                                                                |

The Parameter to the Spin Down Op Code is defined in the table below:

Table 4.27: Spin Down Status value definition

| Spin Down Status Value (UINT8)   | Definition              |
|----------------------------------|-------------------------|
| 0x00                             | Reserved for Future Use |
| 0x01                             | Spin Down Requested     |
| 0x02                             | Success                 |
| 0x03                             | Error                   |
| 0x04                             | Stop Pedaling           |
| 0x05 - 0xFF                      | Reserved for Future Use |


### 4.17.1 Characteristic Behavior

When the Fitness Machine Status characteristic is configured for notification via the Client Characteristic Configuration descriptor, this characteristic shall be notified when new status information is available (e.g., typically within a second after the status change has occurred).

If the status is updated by the user (e.g., the user increases the speed via the UI of the Server), the Server shall notify this characteristic to all connected Clients with the appropriate characteristic value, as defined in Table 4.26.

If the status is updated by a Client (e.g., a Client increases the speed via the Fitness Machine Control Point procedure defined in Section 4.16.2.3), the Server shall notify this characteristic to the other connected Clients, if any.

The Server shall send the Fitness Machine Status notification with the Op Code set to Control Permission Lost to the Client that has lost control (e.g., control has been revoked by the user via the UI of the Server or if another connected Client has requested control).

The Fitness Machine characteristic contains time-sensitive data; thus the requirements for time-sensitive data and data storage defined in Section 4.18 apply.

## 4.18 Requirements for Time-Sensitive Data

The following characteristics contain time-sensitive data:

- Treadmill Data
- Cross Trainer Data
- Step Climber Data
- Stair Climber Data
- Rower Data
- Indoor Bike Data
- Training Status
- Fitness Machine Status

The following requirements apply to these time-sensitive characteristics:

- If a link loss occurs while the Server sends a Data Record in multiple notifications, the Data Record shall be discarded and shall not be sent to the Client if the connection is reestablished.
- Since this service does not provide any data with a time stamp to identify the measurement time (and age) of the data (the Elapsed Time feature refers to the relative time since the training session has started and not to a time stamp that includes the date-time information), the value of the characteristics (mentioned above) that can be notified shall not be stored in the Server if either the connection is not established or if the notification is not successfully transmitted (e.g., due to link loss).


## 4.19 Transmission of a Data Record

For devices that support the low energy feature of Bluetooth, if a Data Record (see Section 4.1) does not exceed the ATT-MTU size and therefore can be sent in one single notification, the More Data bit of the Flags field shall be set to 0 and the associated fields shall be included in the notification.

For devices that support the low energy feature of Bluetooth, if a Data Record (see Section 4.1) exceeds the ATT-MTU size, the first notification used to send this Data Record shall have the More Data bit of the Flags field set to 1. The subsequent notifications used to send this Data Record shall also have the More Data bit of the Flags field set to 1, except for the last notification, which shall have the More Data bit of the Flags field set to 0.

There is no additional requirement for BR/EDR devices due to larger MTU.

