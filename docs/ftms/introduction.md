---
type: Bluetooth service specification section
title: "1 Introduction"
description: "Conformance, dependencies, procedures, errors, and byte transmission order."
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

# 1 Introduction

The Fitness Machine Service (FTMS) exposes training-related data in the sports and fitness environment, which allows a Client to collect training data while a user is exercising with a fitness machine (Server).

The service also exposes the Training Status characteristic (See Section 4.10) in order to provide feedback to the Client about training status (e.g., warming up, low or high intensity phase, etc.) In addition, the service may also expose the Fitness Machine Status characteristic to send information to the Client about the status of the machine (e.g., started by the user, stopped by the user, etc.)

The Fitness Machine Control Point may also be exposed by the Server in order to provide a mechanism to remotely control a fitness machine (e.g., start, stop, increase the speed, etc.)

The types of fitness machines that are currently supported are listed below:

- Treadmill
- Cross Trainer
- Step Climber
- Stair Climber
- Rower
- Indoor Bike

## 1.1 Conformance

Each capability of this specification shall be supported in the specified manner. This specification may provide options for design flexibility, because, for example, some products do not implement every portion of the specification. For each implementation option that is supported, it shall be supported as specified.

## 1.2 Service Dependencies

This service is not dependent upon any other services.

## 1.3 Bluetooth Core Specification Release Compatibility

This specification is compatible with any of the following:

- Bluetooth Core Specification 4.2 or later [1].

## 1.4 GATT Sub-Procedure Requirements

Requirements in this section represent a minimum set of requirements for a Server. Other GATT subprocedures may be used if supported by both Client and Server.

Table 1.1 summarizes additional GATT sub-procedure requirements beyond those required by all GATT Servers.


Table 1.1: GATT Sub-procedure Requirements

| GATT Sub-Procedure         | Requirements   |
|----------------------------|----------------|
| Write Characteristic Value | M              |
| Notification               | M              |
| Indication                 | C.1            |
| Read Long                  | O              |

C.1: Mandatory if the Fitness Machine Control Point is supported; otherwise Optional.

## 1.5 Transport Dependencies

There are no transport restrictions imposed by this service specification.

Where the term BR/EDR is used throughout this document, this also includes the optional use of AMP.

## 1.6 Application Error Codes

This service does not define any Attribute Protocol Application Error codes.

## 1.7 Byte Transmission Order

All characteristics used with this service shall be transmitted with the least significant octet first (i.e., little endian). The least significant octet is identified in the characteristic definitions on the Bluetooth SIG Assigned Numbers webpage [2].

