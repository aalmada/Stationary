using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Client;

public sealed class FtmsTelemetryProcessor(FtmsMachineDataType machineType, LatestTelemetryBuffer buffer)
{
    private readonly Lock sync = new();
    private TelemetrySnapshot? latest;

    public FtmsDecodeStatus Process(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt)
    {
        lock (sync)
        {
            var status = TryDecode(payload, capturedAt, out var snapshot);
            if (status != FtmsDecodeStatus.Success)
            {
                return status;
            }

            latest = Merge(latest, snapshot);
            buffer.TryPublish(latest.Value);
            return FtmsDecodeStatus.Success;
        }
    }

    private FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt, out TelemetrySnapshot snapshot)
    {
        snapshot = default;
        return machineType switch
        {
            FtmsMachineDataType.Treadmill => DecodeTreadmill(payload, capturedAt, out snapshot),
            FtmsMachineDataType.CrossTrainer => DecodeCrossTrainer(payload, capturedAt, out snapshot),
            FtmsMachineDataType.StepClimber => DecodeStepClimber(payload, capturedAt, out snapshot),
            FtmsMachineDataType.StairClimber => DecodeStairClimber(payload, capturedAt, out snapshot),
            FtmsMachineDataType.Rower => DecodeRower(payload, capturedAt, out snapshot),
            FtmsMachineDataType.IndoorBike => DecodeIndoorBike(payload, capturedAt, out snapshot),
            _ => FtmsDecodeStatus.InvalidFlags,
        };
    }

    private FtmsDecodeStatus DecodeTreadmill(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt, out TelemetrySnapshot snapshot)
    {
        var status = TreadmillData.TryDecode(payload, out var data);
        snapshot = new(capturedAt, machineType, data.InstantaneousSpeedKilometersPerHour, null, Available(data.PowerOutput), null, data.HeartRate, data.TotalDistance,
            AverageSpeedKilometersPerHour: data.AverageSpeed is ushort averageSpeed ? averageSpeed / 100d : null,
            InclinationTenths: Available(data.Inclination),
            RampAngleTenths: Available(data.RampAngleSetting),
            PositiveElevationGainMeters: data.PositiveElevationGain,
            NegativeElevationGainMeters: data.NegativeElevationGain,
            InstantaneousPaceSecondsPerKilometre: data.InstantaneousPace,
            AveragePaceSecondsPerKilometre: data.AveragePace,
            MetabolicEquivalentTenths: data.MetabolicEquivalent,
            TotalEnergyKilocalories: Available(data.TotalEnergy),
            EnergyPerHourKilocalories: Available(data.EnergyPerHour),
            EnergyPerMinuteKilocalories: Available(data.EnergyPerMinute),
            ElapsedTimeSeconds: data.ElapsedTime,
            RemainingTimeSeconds: data.RemainingTime,
            ForceOnBeltNewtons: Available(data.ForceOnBelt),
            ReportedFields: GetReportedFields(data));
        return status;
    }

    private FtmsDecodeStatus DecodeCrossTrainer(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt, out TelemetrySnapshot snapshot)
    {
        var status = CrossTrainerData.TryDecode(payload, out var data);
        snapshot = new(capturedAt, machineType, data.InstantaneousSpeedKilometersPerHour, Available(data.StepPerMinute), data.InstantaneousPower, data.ResistanceLevel is sbyte resistance ? (short)(resistance * 10) : null, data.HeartRate, data.TotalDistance,
            AverageSpeedKilometersPerHour: data.AverageSpeed is ushort averageSpeed ? averageSpeed / 100d : null,
            AverageCadenceRpm: Available(data.AverageStepRate),
            AveragePowerWatts: data.AveragePower,
            InclinationTenths: Available(data.Inclination),
            RampAngleTenths: Available(data.RampAngleSetting),
            PositiveElevationGainMeters: data.PositiveElevationGain,
            NegativeElevationGainMeters: data.NegativeElevationGain,
            StrideCount: data.StrideCount,
            MetabolicEquivalentTenths: data.MetabolicEquivalent,
            TotalEnergyKilocalories: Available(data.TotalEnergy),
            EnergyPerHourKilocalories: Available(data.EnergyPerHour),
            EnergyPerMinuteKilocalories: Available(data.EnergyPerMinute),
            ElapsedTimeSeconds: data.ElapsedTime,
            RemainingTimeSeconds: data.RemainingTime,
            IsMovingBackward: data.IsMovingBackward,
            ReportedFields: GetReportedFields(data));
        return status;
    }

    private FtmsDecodeStatus DecodeStepClimber(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt, out TelemetrySnapshot snapshot)
    {
        var status = StepClimberData.TryDecode(payload, out var data);
        snapshot = new(capturedAt, machineType, null, Available(data.StepPerMinute), null, null, data.HeartRate, null,
            AverageCadenceRpm: Available(data.AverageStepRate),
            StepCount: data.StepCount,
            PositiveElevationGainMeters: data.PositiveElevationGain,
            MetabolicEquivalentTenths: data.MetabolicEquivalent,
            TotalEnergyKilocalories: Available(data.TotalEnergy),
            EnergyPerHourKilocalories: Available(data.EnergyPerHour),
            EnergyPerMinuteKilocalories: Available(data.EnergyPerMinute),
            ElapsedTimeSeconds: data.ElapsedTime,
            RemainingTimeSeconds: data.RemainingTime,
            Floors: data.Floors,
            ReportedFields: GetReportedFields(data));
        return status;
    }

    private FtmsDecodeStatus DecodeStairClimber(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt, out TelemetrySnapshot snapshot)
    {
        var status = StairClimberData.TryDecode(payload, out var data);
        snapshot = new(capturedAt, machineType, null, Available(data.StepPerMinute), null, null, data.HeartRate, null,
            AverageCadenceRpm: Available(data.AverageStepRate),
            StrideCount: data.StrideCount,
            PositiveElevationGainMeters: data.PositiveElevationGain,
            MetabolicEquivalentTenths: data.MetabolicEquivalent,
            TotalEnergyKilocalories: Available(data.TotalEnergy),
            EnergyPerHourKilocalories: Available(data.EnergyPerHour),
            EnergyPerMinuteKilocalories: Available(data.EnergyPerMinute),
            ElapsedTimeSeconds: data.ElapsedTime,
            RemainingTimeSeconds: data.RemainingTime,
            Floors: data.Floors,
            ReportedFields: GetReportedFields(data));
        return status;
    }

    private FtmsDecodeStatus DecodeRower(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt, out TelemetrySnapshot snapshot)
    {
        var status = RowerData.TryDecode(payload, out var data);
        snapshot = new(capturedAt, machineType, null, data.StrokeRate, data.InstantaneousPower, data.ResistanceLevel is sbyte resistance ? (short)(resistance * 10) : null, data.HeartRate, data.TotalDistance,
            AverageCadenceRpm: data.AverageStrokeRate,
            AveragePowerWatts: data.AveragePower,
            InstantaneousPaceSecondsPerKilometre: data.InstantaneousPace,
            AveragePaceSecondsPerKilometre: data.AveragePace,
            StrokeCount: data.StrokeCount,
            MetabolicEquivalentTenths: data.MetabolicEquivalent,
            TotalEnergyKilocalories: Available(data.TotalEnergy),
            EnergyPerHourKilocalories: Available(data.EnergyPerHour),
            EnergyPerMinuteKilocalories: Available(data.EnergyPerMinute),
            ElapsedTimeSeconds: data.ElapsedTime,
            RemainingTimeSeconds: data.RemainingTime,
            ReportedFields: GetReportedFields(data));
        return status;
    }

    private FtmsDecodeStatus DecodeIndoorBike(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt, out TelemetrySnapshot snapshot)
    {
        var status = IndoorBikeData.TryDecode(payload, out var data);
        snapshot = new(capturedAt, machineType, data.InstantaneousSpeedKilometersPerHour, data.InstantaneousCadenceRpm, data.InstantaneousPower, data.ResistanceLevel, data.HeartRate, data.TotalDistance,
            AverageSpeedKilometersPerHour: data.AverageSpeed is ushort averageSpeed ? averageSpeed / 100d : null,
            AverageCadenceRpm: data.AverageCadence is ushort averageCadence ? averageCadence / 2d : null,
            AveragePowerWatts: data.AveragePower,
            MetabolicEquivalentTenths: data.MetabolicEquivalent,
            TotalEnergyKilocalories: Available(data.TotalEnergy),
            EnergyPerHourKilocalories: Available(data.EnergyPerHour),
            EnergyPerMinuteKilocalories: Available(data.EnergyPerMinute),
            ElapsedTimeSeconds: data.ElapsedTime,
            RemainingTimeSeconds: data.RemainingTime,
            ReportedFields: GetReportedFields(data));
        return status;
    }

    private static TelemetryField GetReportedFields(TreadmillData data) =>
        Field(!data.MoreData, TelemetryField.Speed) |
        Field(data.AverageSpeed.HasValue, TelemetryField.AverageSpeed) |
        Field(data.TotalDistance.HasValue, TelemetryField.TotalDistance) |
        Field(data.Inclination.HasValue, TelemetryField.Inclination) |
        Field(data.RampAngleSetting.HasValue, TelemetryField.RampAngle) |
        Field(data.PositiveElevationGain.HasValue, TelemetryField.PositiveElevation) |
        Field(data.NegativeElevationGain.HasValue, TelemetryField.NegativeElevation) |
        Field(data.InstantaneousPace.HasValue, TelemetryField.InstantaneousPace) |
        Field(data.AveragePace.HasValue, TelemetryField.AveragePace) |
        EnergyFields(data.TotalEnergy, data.EnergyPerHour, data.EnergyPerMinute) |
        Field(data.HeartRate.HasValue, TelemetryField.HeartRate) |
        Field(data.MetabolicEquivalent.HasValue, TelemetryField.MetabolicEquivalent) |
        Field(data.ElapsedTime.HasValue, TelemetryField.ElapsedTime) |
        Field(data.RemainingTime.HasValue, TelemetryField.RemainingTime) |
        Field(data.ForceOnBelt.HasValue, TelemetryField.ForceOnBelt) |
        Field(data.PowerOutput.HasValue, TelemetryField.Power);

    private static TelemetryField GetReportedFields(CrossTrainerData data) =>
        Field(!data.MoreData, TelemetryField.Speed) |
        Field(data.AverageSpeed.HasValue, TelemetryField.AverageSpeed) |
        Field(data.TotalDistance.HasValue, TelemetryField.TotalDistance) |
        Field(data.StepPerMinute.HasValue, TelemetryField.Cadence) |
        Field(data.AverageStepRate.HasValue, TelemetryField.AverageCadence) |
        Field(data.StrideCount.HasValue, TelemetryField.StrideCount) |
        Field(data.PositiveElevationGain.HasValue, TelemetryField.PositiveElevation) |
        Field(data.NegativeElevationGain.HasValue, TelemetryField.NegativeElevation) |
        Field(data.Inclination.HasValue, TelemetryField.Inclination) |
        Field(data.RampAngleSetting.HasValue, TelemetryField.RampAngle) |
        Field(data.ResistanceLevel.HasValue, TelemetryField.Resistance) |
        Field(data.InstantaneousPower.HasValue, TelemetryField.Power) |
        Field(data.AveragePower.HasValue, TelemetryField.AveragePower) |
        EnergyFields(data.TotalEnergy, data.EnergyPerHour, data.EnergyPerMinute) |
        Field(data.HeartRate.HasValue, TelemetryField.HeartRate) |
        Field(data.MetabolicEquivalent.HasValue, TelemetryField.MetabolicEquivalent) |
        Field(data.ElapsedTime.HasValue, TelemetryField.ElapsedTime) |
        Field(data.RemainingTime.HasValue, TelemetryField.RemainingTime) |
        TelemetryField.Direction;

    private static TelemetryField GetReportedFields(StepClimberData data) =>
        Field(!data.MoreData, TelemetryField.Floors | TelemetryField.StepCount) |
        Field(data.StepPerMinute.HasValue, TelemetryField.Cadence) |
        Field(data.AverageStepRate.HasValue, TelemetryField.AverageCadence) |
        Field(data.PositiveElevationGain.HasValue, TelemetryField.PositiveElevation) |
        EnergyFields(data.TotalEnergy, data.EnergyPerHour, data.EnergyPerMinute) |
        CommonFields(data.HeartRate, data.MetabolicEquivalent, data.ElapsedTime, data.RemainingTime);

    private static TelemetryField GetReportedFields(StairClimberData data) =>
        Field(!data.MoreData, TelemetryField.Floors) |
        Field(data.StepPerMinute.HasValue, TelemetryField.Cadence) |
        Field(data.AverageStepRate.HasValue, TelemetryField.AverageCadence) |
        Field(data.PositiveElevationGain.HasValue, TelemetryField.PositiveElevation) |
        Field(data.StrideCount.HasValue, TelemetryField.StrideCount) |
        EnergyFields(data.TotalEnergy, data.EnergyPerHour, data.EnergyPerMinute) |
        CommonFields(data.HeartRate, data.MetabolicEquivalent, data.ElapsedTime, data.RemainingTime);

    private static TelemetryField GetReportedFields(RowerData data) =>
        Field(!data.MoreData, TelemetryField.Cadence | TelemetryField.StrokeCount) |
        Field(data.AverageStrokeRate.HasValue, TelemetryField.AverageCadence) |
        Field(data.TotalDistance.HasValue, TelemetryField.TotalDistance) |
        Field(data.InstantaneousPace.HasValue, TelemetryField.InstantaneousPace) |
        Field(data.AveragePace.HasValue, TelemetryField.AveragePace) |
        Field(data.InstantaneousPower.HasValue, TelemetryField.Power) |
        Field(data.AveragePower.HasValue, TelemetryField.AveragePower) |
        Field(data.ResistanceLevel.HasValue, TelemetryField.Resistance) |
        EnergyFields(data.TotalEnergy, data.EnergyPerHour, data.EnergyPerMinute) |
        CommonFields(data.HeartRate, data.MetabolicEquivalent, data.ElapsedTime, data.RemainingTime);

    private static TelemetryField GetReportedFields(IndoorBikeData data) =>
        Field(!data.MoreData, TelemetryField.Speed) |
        Field(data.AverageSpeed.HasValue, TelemetryField.AverageSpeed) |
        Field(data.InstantaneousCadence.HasValue, TelemetryField.Cadence) |
        Field(data.AverageCadence.HasValue, TelemetryField.AverageCadence) |
        Field(data.TotalDistance.HasValue, TelemetryField.TotalDistance) |
        Field(data.ResistanceLevel.HasValue, TelemetryField.Resistance) |
        Field(data.InstantaneousPower.HasValue, TelemetryField.Power) |
        Field(data.AveragePower.HasValue, TelemetryField.AveragePower) |
        EnergyFields(data.TotalEnergy, data.EnergyPerHour, data.EnergyPerMinute) |
        CommonFields(data.HeartRate, data.MetabolicEquivalent, data.ElapsedTime, data.RemainingTime);

    private static TelemetryField CommonFields(byte? heartRate, byte? metabolicEquivalent, ushort? elapsedTime, ushort? remainingTime) =>
        Field(heartRate.HasValue, TelemetryField.HeartRate) |
        Field(metabolicEquivalent.HasValue, TelemetryField.MetabolicEquivalent) |
        Field(elapsedTime.HasValue, TelemetryField.ElapsedTime) |
        Field(remainingTime.HasValue, TelemetryField.RemainingTime);

    private static TelemetryField EnergyFields(ushort? total, ushort? perHour, byte? perMinute) =>
        Field(total.HasValue, TelemetryField.TotalEnergy) |
        Field(perHour.HasValue, TelemetryField.EnergyPerHour) |
        Field(perMinute.HasValue, TelemetryField.EnergyPerMinute);

    private static TelemetryField Field(bool present, TelemetryField field) => present ? field : TelemetryField.None;

    private static TelemetrySnapshot Merge(TelemetrySnapshot? previous, TelemetrySnapshot current) => previous is not TelemetrySnapshot value
        ? current
        : current with
        {
            SpeedKilometersPerHour = MergeField(current, value, TelemetryField.Speed, current.SpeedKilometersPerHour, value.SpeedKilometersPerHour),
            CadenceRpm = MergeField(current, value, TelemetryField.Cadence, current.CadenceRpm, value.CadenceRpm),
            PowerWatts = MergeField(current, value, TelemetryField.Power, current.PowerWatts, value.PowerWatts),
            ResistanceTenths = MergeField(current, value, TelemetryField.Resistance, current.ResistanceTenths, value.ResistanceTenths),
            HeartRateBeatsPerMinute = MergeField(current, value, TelemetryField.HeartRate, current.HeartRateBeatsPerMinute, value.HeartRateBeatsPerMinute),
            TotalDistanceMeters = MergeField(current, value, TelemetryField.TotalDistance, current.TotalDistanceMeters, value.TotalDistanceMeters),
            AverageSpeedKilometersPerHour = MergeField(current, value, TelemetryField.AverageSpeed, current.AverageSpeedKilometersPerHour, value.AverageSpeedKilometersPerHour),
            AverageCadenceRpm = MergeField(current, value, TelemetryField.AverageCadence, current.AverageCadenceRpm, value.AverageCadenceRpm),
            AveragePowerWatts = MergeField(current, value, TelemetryField.AveragePower, current.AveragePowerWatts, value.AveragePowerWatts),
            InclinationTenths = MergeField(current, value, TelemetryField.Inclination, current.InclinationTenths, value.InclinationTenths),
            RampAngleTenths = MergeField(current, value, TelemetryField.RampAngle, current.RampAngleTenths, value.RampAngleTenths),
            PositiveElevationGainMeters = MergeField(current, value, TelemetryField.PositiveElevation, current.PositiveElevationGainMeters, value.PositiveElevationGainMeters),
            NegativeElevationGainMeters = MergeField(current, value, TelemetryField.NegativeElevation, current.NegativeElevationGainMeters, value.NegativeElevationGainMeters),
            InstantaneousPaceSecondsPerKilometre = MergeField(current, value, TelemetryField.InstantaneousPace, current.InstantaneousPaceSecondsPerKilometre, value.InstantaneousPaceSecondsPerKilometre),
            AveragePaceSecondsPerKilometre = MergeField(current, value, TelemetryField.AveragePace, current.AveragePaceSecondsPerKilometre, value.AveragePaceSecondsPerKilometre),
            StepCount = MergeField(current, value, TelemetryField.StepCount, current.StepCount, value.StepCount),
            StrideCount = MergeField(current, value, TelemetryField.StrideCount, current.StrideCount, value.StrideCount),
            StrokeCount = MergeField(current, value, TelemetryField.StrokeCount, current.StrokeCount, value.StrokeCount),
            MetabolicEquivalentTenths = MergeField(current, value, TelemetryField.MetabolicEquivalent, current.MetabolicEquivalentTenths, value.MetabolicEquivalentTenths),
            TotalEnergyKilocalories = MergeField(current, value, TelemetryField.TotalEnergy, current.TotalEnergyKilocalories, value.TotalEnergyKilocalories),
            EnergyPerHourKilocalories = MergeField(current, value, TelemetryField.EnergyPerHour, current.EnergyPerHourKilocalories, value.EnergyPerHourKilocalories),
            EnergyPerMinuteKilocalories = MergeField(current, value, TelemetryField.EnergyPerMinute, current.EnergyPerMinuteKilocalories, value.EnergyPerMinuteKilocalories),
            ElapsedTimeSeconds = MergeField(current, value, TelemetryField.ElapsedTime, current.ElapsedTimeSeconds, value.ElapsedTimeSeconds),
            RemainingTimeSeconds = MergeField(current, value, TelemetryField.RemainingTime, current.RemainingTimeSeconds, value.RemainingTimeSeconds),
            ForceOnBeltNewtons = MergeField(current, value, TelemetryField.ForceOnBelt, current.ForceOnBeltNewtons, value.ForceOnBeltNewtons),
            Floors = MergeField(current, value, TelemetryField.Floors, current.Floors, value.Floors),
            IsMovingBackward = MergeField(current, value, TelemetryField.Direction, current.IsMovingBackward, value.IsMovingBackward),
        };

    private static T? MergeField<T>(TelemetrySnapshot current, TelemetrySnapshot previous, TelemetryField field, T? currentValue, T? previousValue)
        where T : struct => current.ReportedFields.HasFlag(field) ? currentValue : previousValue;

    private static ushort? Available(ushort? value) => value is ushort.MaxValue ? null : value;

    private static short? Available(short? value) => value is short.MaxValue ? null : value;

    private static byte? Available(byte? value) => value is byte.MaxValue ? null : value;
}