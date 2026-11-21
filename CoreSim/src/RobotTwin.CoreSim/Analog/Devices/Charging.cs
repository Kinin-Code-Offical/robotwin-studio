using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>
    /// [usbc] Lithium protection board (DW01 + dual FET, or a multi-cell BMS): watches the cell stack and
    /// opens the pack's negative FET on over-discharge, over-charge or over-current, the way the chip on
    /// every protected 18650 / USB-C pack does. The FET's body diode (a separate <see cref="Diode"/>) still
    /// lets a charger push current in after an over-discharge cut, so plugging USB-C revives a flat pack.
    /// It stamps nothing itself: it drives the FET <see cref="Switch"/> between the cells and the pack's −.
    /// </summary>
    public sealed class LithiumProtection : AnalogDevice
    {
        private double _overTime;
        private double _retryAt = double.NaN;

        public LithiumProtection(string id, int cellPlus, int cellMinus, Switch fet, int cells) : base(id)
        {
            CellPlus = cellPlus;
            CellMinus = cellMinus;
            Fet = fet;
            Cells = Math.Max(1, cells);
        }

        public int CellPlus { get; }
        public int CellMinus { get; }
        public Switch Fet { get; }
        public int Cells { get; }

        /// <summary>Per-cell thresholds (DW01A typical).</summary>
        public double OverDischargeV { get; set; } = 2.4;

        public double OverChargeV { get; set; } = 4.28;

        /// <summary>Discharge current that trips the FET after <see cref="OverCurrentDelay"/>.</summary>
        public double OverCurrentA { get; set; } = 3.0;

        public double OverCurrentDelay { get; set; } = 0.01;

        /// <summary>Why the pack is cut off ("" while it conducts).</summary>
        public string Tripped { get; private set; } = string.Empty;

        public double CellVoltage { get; private set; }

        public override void Stamp(StampContext ctx)
        {
        }

        public override void Reset()
        {
            _overTime = 0;
            _retryAt = double.NaN;
            Tripped = string.Empty;
            Fet.Closed = true;
        }

        public override void Accept(AcceptContext ctx)
        {
            CellVoltage = ctx.V(CellPlus, CellMinus) / Cells;
            if (ctx.IsDc) return;
            // Discharge current flows pack − → cell − through the FET (negative Switch current, A = cell −).
            double discharge = -Fet.Current;
            if (Tripped.Length == 0)
            {
                string reason = string.Empty;
                if (CellVoltage < OverDischargeV) reason = $"aşırı deşarj koruması: hücre {CellVoltage:F2} V";
                else if (CellVoltage > OverChargeV) reason = $"aşırı şarj koruması: hücre {CellVoltage:F2} V";
                else
                {
                    _overTime = discharge > OverCurrentA ? _overTime + ctx.Dt : 0;
                    if (_overTime >= OverCurrentDelay)
                    {
                        reason = $"aşırı akım koruması: {discharge:F1} A";
                        _retryAt = ctx.Time + 1.0;
                    }
                }

                if (reason.Length > 0)
                {
                    Tripped = reason;
                    Fet.Closed = false;
                    ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.StateChange, "koruma devresi kesti: " + reason));
                    ctx.RequestDiscontinuity();
                }

                return;
            }

            // Release: after an over-discharge the chip sleeps until a charger is connected (its current then
            // flows through the FET's body diode, pulling the cell − above the pack −); an overcharge clears
            // once the cell settles; an over-current cut retries after a second.
            bool charger = ctx.V(Fet.A, Fet.B) > 0.3;
            bool release = Tripped.StartsWith("aşırı deşarj", StringComparison.Ordinal) ? charger
                : Tripped.StartsWith("aşırı şarj", StringComparison.Ordinal) ? CellVoltage < 4.1
                : !double.IsNaN(_retryAt) && ctx.Time >= _retryAt;
            if (!release) return;
            Tripped = string.Empty;
            _overTime = 0;
            _retryAt = double.NaN;
            Fet.Closed = true;
            ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.StateChange, "koruma devresi yeniden bağladı"));
            ctx.RequestDiscontinuity();
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "VCELL", CellVoltage);
            sink.Write(Id, "ON", Tripped.Length == 0 ? 1.0 : 0.0);
        }
    }
}
