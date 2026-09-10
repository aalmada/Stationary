---
type: Bluetooth service specification section
title: "3 Service Advertising Data"
description: "Service data advertising format, flags, machine types, and byte ordering."
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

# 3 Service Advertising Data

This section defines the advertising data requirements for this service.

## 3.1 Service Data AD Type

This section describes the contents of and requirements for the Service Data AD Type that enables a Client to determine the type of fitness machine (Server) that is advertising. This is to avoid initiating a connection between Servers and Clients that do not support that particular type of fitness machine (Server).

As permitted in the Core Specification and the Core Specification Supplement [1], the Service Data AD Type may also be present in the Extended Inquiry Response (EIR).

The definition of the Service Data AD Type is shown in Table 3.1.

Refer to Section 3.2 for details regarding byte ordering.

Table 3.1: Service Data AD Type

| Service Data AD Type Field               | Data Type   |   Size (octets) | Requirement   |
|------------------------------------------|-------------|-----------------|---------------|
| Service Data AD Type (See [3])           | UINT8       |               1 | M             |
| Fitness Machine Service UUID (See [2])   | UINT16      |               2 | M             |
| Flags (See Section 3.1.1)                | UINT8       |               1 | M             |
| Fitness Machine Type (See Section 3.1.2) | UINT16      |               2 | M             |

### 3.1.1 Flags Field

The Flags field shall be included in the Service Data AD Type.

Table 3.2: Flags Field

| Bit   | Definition                                  |
|-------|---------------------------------------------|
| 0     | Fitness Machine Available: 0: False 1: True |
| 1-7   | Reserved for Future Use                     |


### 3.1.2 Fitness Machine Type Field

The Fitness Machine Type field shall be included in the Service Data AD Type.

When a bit is set to 1 (True) in the Fitness Machine Type field, the Server supports the associated feature. If the Server does not support the relevant feature, the associated feature bit shall be set to 0 (False), as defined in Table 3.3:

Table 3.3: Fitness Machine Type Field

| Bit Number   | Definition                               |
|--------------|------------------------------------------|
| 0            | Treadmill Supported 0: False 1: True     |
| 1            | Cross Trainer Supported 0: False 1: True |
| 2            | Step Climber Supported 0: False 1: True  |
| 3            | Stair Climber Supported 0: False 1: True |
| 4            | Rower Supported 0: False 1: True         |
| 5            | Indoor Bike Supported 0: False 1: True   |
| 6 - 15       | Reserved for Future Use                  |

## 3.2 Byte Ordering

Where characteristics and descriptors are comprised of multiple bytes (shown in several tables within this document), the Least Significant Octet (LSO) is defined as the eight low-numbered bits (i.e., bits 0 to 7) of the topmost field in the tables. The Most Significant Octet (MSO) is defined as the high-numbered bits of the bottommost field in the tables.

