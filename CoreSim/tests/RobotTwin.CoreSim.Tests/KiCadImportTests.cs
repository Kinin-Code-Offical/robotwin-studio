using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Design")]
    public class KiCadImportTests : IDisposable
    {
        private const double Tol = 1e-6;
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rw-kicad-import-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        // ================================================================== samples (KiCad 5–8 syntax, written by hand)

        /// <summary>KiCad 8 DIP-8 with long oval pads, a notch arc and a courtyard made of lines.</summary>
        private const string Dip8 = """
            (footprint "DIP-8_W7.62mm_LongPads"
            	(version 20240108)
            	(generator "pcbnew")
            	(generator_version "8.0")
            	(layer "F.Cu")
            	(descr "8-lead though-hole mounted DIP package, row spacing 7.62 mm (300 mils), LongPads")
            	(tags "THT DIP DIL PDIP 2.54mm 7.62mm 300mil LongPads")
            	(property "Reference" "REF**"
            		(at 3.81 -2.33 0)
            		(layer "F.SilkS")
            		(uuid "0f1e1fb5-8a2f-4b6d-9d0e-3a3c33c1a001")
            		(effects (font (size 1 1) (thickness 0.15)))
            	)
            	(property "Value" "DIP-8_W7.62mm_LongPads"
            		(at 3.81 9.95 0)
            		(layer "F.Fab")
            		(uuid "0f1e1fb5-8a2f-4b6d-9d0e-3a3c33c1a002")
            		(effects (font (size 1 1) (thickness 0.15)))
            	)
            	(attr through_hole)
            	(fp_line (start 1.56 -1.33) (end 1.56 8.95) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "a1"))
            	(fp_line (start 1.56 8.95) (end 6.06 8.95) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "a2"))
            	(fp_line (start 2.81 -1.33) (end 1.56 -1.33) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "a3"))
            	(fp_line (start 6.06 -1.33) (end 4.81 -1.33) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "a4"))
            	(fp_line (start 6.06 8.95) (end 6.06 -1.33) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "a5"))
            	(fp_line (start -1.45 -1.55) (end -1.45 9.15) (stroke (width 0.05) (type solid)) (layer "F.CrtYd") (uuid "c1"))
            	(fp_line (start -1.45 9.15) (end 9.1 9.15) (stroke (width 0.05) (type solid)) (layer "F.CrtYd") (uuid "c2"))
            	(fp_line (start 9.1 -1.55) (end -1.45 -1.55) (stroke (width 0.05) (type solid)) (layer "F.CrtYd") (uuid "c3"))
            	(fp_line (start 9.1 9.15) (end 9.1 -1.55) (stroke (width 0.05) (type solid)) (layer "F.CrtYd") (uuid "c4"))
            	(fp_line (start 0.635 -0.27) (end 1.635 -1.27) (stroke (width 0.1) (type solid)) (layer "F.Fab") (uuid "f1"))
            	(fp_line (start 0.635 8.89) (end 0.635 -0.27) (stroke (width 0.1) (type solid)) (layer "F.Fab") (uuid "f2"))
            	(fp_arc (start 4.81 -1.33) (mid 3.81 -0.33) (end 2.81 -1.33) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "a6"))
            	(fp_text user "${REFERENCE}" (at 3.81 3.81 0) (layer "F.Fab") (uuid "t1") (effects (font (size 1 1) (thickness 0.15))))
            	(pad "1" thru_hole rect (at 0 0) (size 2.4 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p1"))
            	(pad "2" thru_hole oval (at 0 2.54) (size 2.4 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p2"))
            	(pad "3" thru_hole oval (at 0 5.08) (size 2.4 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p3"))
            	(pad "4" thru_hole oval (at 0 7.62) (size 2.4 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p4"))
            	(pad "5" thru_hole oval (at 7.62 7.62) (size 2.4 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p5"))
            	(pad "6" thru_hole oval (at 7.62 5.08) (size 2.4 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p6"))
            	(pad "7" thru_hole oval (at 7.62 2.54) (size 2.4 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p7"))
            	(pad "8" thru_hole oval (at 7.62 0) (size 2.4 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p8"))
            	(model "${KICAD8_3DMODEL_DIR}/Package_DIP.3dshapes/DIP-8_W7.62mm.wrl"
            		(offset (xyz 0 0 0))
            		(scale (xyz 1 1 1))
            		(rotate (xyz 0 0 0))
            	)
            )
            """;

        /// <summary>KiCad 8 SOT-23: roundrect SMD pads, filled silk triangle, courtyard as fp_poly.</summary>
        private const string Sot23 = """
            (footprint "SOT-23"
            	(version 20240108)
            	(generator "pcbnew")
            	(generator_version "8.0")
            	(layer "F.Cu")
            	(descr "SOT, 3 Pin (https://www.jedec.org/system/files/docs/to-236h.pdf variant AB), generated with kicad-footprint-generator ipc_gullwing_generator.py")
            	(tags "SOT TO_SOT_SMD")
            	(property "Reference" "REF**" (at 0 -2.4 0) (layer "F.SilkS") (uuid "r") (effects (font (size 1 1) (thickness 0.15))))
            	(property "Value" "SOT-23" (at 0 2.4 0) (layer "F.Fab") (uuid "v") (effects (font (size 1 1) (thickness 0.15))))
            	(attr smd)
            	(fp_line (start 0 -1.56) (end -0.65 -1.56) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "s1"))
            	(fp_line (start 0 -1.56) (end 0.65 -1.56) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "s2"))
            	(fp_line (start 0 1.56) (end -0.65 1.56) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "s3"))
            	(fp_line (start 0 1.56) (end 0.65 1.56) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "s4"))
            	(fp_poly (pts (xy -1.1625 -1.51) (xy -1.4025 -1.84) (xy -0.9225 -1.84) (xy -1.1625 -1.51))
            		(stroke (width 0.12) (type solid)) (fill solid) (layer "F.SilkS") (uuid "s5"))
            	(fp_poly (pts (xy -1.92 -1.7) (xy 1.92 -1.7) (xy 1.92 1.7) (xy -1.92 1.7))
            		(stroke (width 0.05) (type solid)) (fill none) (layer "F.CrtYd") (uuid "c1"))
            	(fp_poly (pts (xy -0.325 -1.45) (xy 0.65 -1.45) (xy 0.65 1.45) (xy -0.65 1.45) (xy -0.65 -1.125))
            		(stroke (width 0.1) (type solid)) (fill none) (layer "F.Fab") (uuid "f1"))
            	(pad "1" smd roundrect (at -0.9375 -0.95) (size 1.475 0.6) (layers "F.Cu" "F.Paste" "F.Mask") (roundrect_rratio 0.25) (uuid "p1"))
            	(pad "2" smd roundrect (at -0.9375 0.95) (size 1.475 0.6) (layers "F.Cu" "F.Paste" "F.Mask") (roundrect_rratio 0.25) (uuid "p2"))
            	(pad "3" smd roundrect (at 0.9375 0) (size 1.475 0.6) (layers "F.Cu" "F.Paste" "F.Mask") (roundrect_rratio 0.25) (uuid "p3"))
            	(model "${KICAD8_3DMODEL_DIR}/Package_TO_SOT_SMD.3dshapes/SOT-23.wrl" (offset (xyz 0 0 0)) (scale (xyz 1 1 1)) (rotate (xyz 0 0 0)))
            )
            """;

        /// <summary>KiCad 8 0805 chip resistor, courtyard as fp_rect.</summary>
        private const string R0805 = """
            (footprint "R_0805_2012Metric"
            	(version 20240108)
            	(generator "pcbnew")
            	(generator_version "8.0")
            	(layer "F.Cu")
            	(descr "Resistor SMD 0805 (2012 Metric), square (rectangular) end terminal, IPC_7351 nominal, generated with kicad-footprint-generator")
            	(tags "resistor")
            	(property "Reference" "REF**" (at 0 -1.65 0) (layer "F.SilkS") (uuid "r") (effects (font (size 1 1) (thickness 0.15))))
            	(property "Value" "R_0805_2012Metric" (at 0 1.65 0) (layer "F.Fab") (uuid "v") (effects (font (size 1 1) (thickness 0.15))))
            	(attr smd)
            	(fp_line (start -0.227064 -0.735) (end 0.227064 -0.735) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "s1"))
            	(fp_line (start -0.227064 0.735) (end 0.227064 0.735) (stroke (width 0.12) (type solid)) (layer "F.SilkS") (uuid "s2"))
            	(fp_rect (start -1.68 -0.95) (end 1.68 0.95) (stroke (width 0.05) (type solid)) (fill none) (layer "F.CrtYd") (uuid "c1"))
            	(fp_rect (start -1 -0.625) (end 1 0.625) (stroke (width 0.1) (type solid)) (fill none) (layer "F.Fab") (uuid "f1"))
            	(pad "1" smd roundrect (at -0.9125 0) (size 1.025 1.4) (layers "F.Cu" "F.Paste" "F.Mask") (roundrect_rratio 0.243902) (uuid "p1"))
            	(pad "2" smd roundrect (at 0.9125 0) (size 1.025 1.4) (layers "F.Cu" "F.Paste" "F.Mask") (roundrect_rratio 0.243902) (uuid "p2"))
            	(model "${KICAD8_3DMODEL_DIR}/Resistor_SMD.3dshapes/R_0805_2012Metric.wrl" (offset (xyz 0 0 0)) (scale (xyz 1 1 1)) (rotate (xyz 0 0 0)))
            )
            """;

        /// <summary>KiCad 6-style footprint (stroke as (width)) with rotated, trapezoid, custom, bottom and paste-only pads; no courtyard.</summary>
        private const string Rotated = """
            (footprint "Test_RotatedPads" (version 20211014) (generator pcbnew)
              (layer "F.Cu")
              (descr "Pads at odd angles")
              (attr smd)
              (fp_circle (center 5 5) (end 6.5 5) (layer "F.SilkS") (width 0.12) (fill none) (tstamp 1))
              (pad "1" smd rect (at 0 0 90) (size 2 1) (layers "F.Cu" "F.Paste" "F.Mask") (tstamp 2))
              (pad "2" thru_hole oval (at 5 0 270) (size 3 1.5) (drill oval 0.8 2.0) (layers *.Cu *.Mask) (tstamp 3))
              (pad "3" smd trapezoid (at 0 5) (size 2 1) (rect_delta 0 0.4) (layers "F.Cu" "F.Mask") (tstamp 4))
              (pad "4" smd custom (at 5 5 90) (size 1 1) (layers "F.Cu" "F.Mask")
                (options (clearance outline) (anchor rect))
                (primitives
                  (gr_poly (pts (xy -0.5 -0.5) (xy 2.5 -0.5) (xy 2.5 0.5) (xy -0.5 0.5)) (width 0) (fill yes))
                ) (tstamp 5))
              (pad "5" smd rect (at 10 0 45) (size 2 2) (layers "F.Cu" "F.Paste" "F.Mask") (tstamp 6))
              (pad "6" smd circle (at 10 5) (size 1.2 1.2) (layers "B.Cu" "B.Paste" "B.Mask") (tstamp 7))
              (pad "" smd rect (at 12 5) (size 0.5 0.5) (layers "F.Paste") (tstamp 8))
            )
            """;

        /// <summary>KiCad 8 JST XH with a non-plated locating boss and a 3D model.</summary>
        private const string JstXh = """
            (footprint "JST_XH_B2B-XH-AM_1x02_P2.50mm_Vertical"
            	(version 20240108)
            	(generator "pcbnew")
            	(generator_version "8.0")
            	(layer "F.Cu")
            	(descr "JST XH series connector, B2B-XH-AM, with boss (http://www.jst-mfg.com/product/pdf/eng/eXH.pdf), generated with kicad-footprint-generator")
            	(tags "connector JST XH vertical boss")
            	(property "Reference" "REF**" (at 1.25 -3.55 0) (layer "F.SilkS") (uuid "r") (effects (font (size 1 1) (thickness 0.15))))
            	(property "Value" "JST_XH_B2B-XH-AM_1x02_P2.50mm_Vertical" (at 1.25 4.6 0) (layer "F.Fab") (uuid "v") (effects (font (size 1 1) (thickness 0.15))))
            	(attr through_hole)
            	(fp_rect (start -2.56 -2.46) (end 5.06 3.51) (stroke (width 0.12) (type solid)) (fill none) (layer "F.SilkS") (uuid "s1"))
            	(fp_rect (start -2.95 -2.85) (end 5.45 3.9) (stroke (width 0.05) (type solid)) (fill none) (layer "F.CrtYd") (uuid "c1"))
            	(pad "" np_thru_hole circle (at 1.25 2.0) (size 1.2 1.2) (drill 1.2) (layers "*.Cu" "*.Mask") (uuid "h1"))
            	(pad "1" thru_hole roundrect (at 0 0) (size 1.7 1.95) (drill 0.95) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (roundrect_rratio 0.147059) (uuid "p1"))
            	(pad "2" thru_hole oval (at 2.5 0) (size 1.7 1.95) (drill 0.95) (layers "*.Cu" "*.Mask") (remove_unused_layers no) (uuid "p2"))
            	(model "${KICAD8_3DMODEL_DIR}/Connector_JST.3dshapes/JST_XH_B2B-XH-AM_1x02_P2.50mm_Vertical.wrl"
            		(offset (xyz 0 0 0))
            		(scale (xyz 1 1 1))
            		(rotate (xyz 0 0 0))
            	)
            )
            """;

        /// <summary>KiCad 5 (module ...) with bare atoms, a circular courtyard and an old-style arc.</summary>
        private const string LegacyMountingHole = """
            (module MountingHole_3.2mm_M3 (layer F.Cu) (tedit 56D1B4CB)
              (descr "Mounting Hole 3.2mm, no annular, M3")
              (tags "mounting hole 3.2mm no annular m3")
              (attr virtual)
              (fp_text reference REF** (at 0 -4.2) (layer F.SilkS)
                (effects (font (size 1 1) (thickness 0.15)))
              )
              (fp_text value MountingHole_3.2mm_M3 (at 0 4.2) (layer F.Fab)
                (effects (font (size 1 1) (thickness 0.15)))
              )
              (fp_circle (center 0 0) (end 3.2 0) (layer Cmts.User) (width 0.15))
              (fp_circle (center 0 0) (end 3.45 0) (layer F.CrtYd) (width 0.05))
              (fp_arc (start 0 0) (end 2 0) (angle 90) (layer F.SilkS) (width 0.12))
              (pad 1 np_thru_hole circle (at 0 0) (size 3.2 3.2) (drill 3.2) (layers *.Cu *.Mask))
            )
            """;

        /// <summary>
        /// KiCad 8 symbol library (C_Polarized in KiCad 6 syntax: (id n), bare "hide", ki_description):
        /// R, C_Polarized, LED, Q_NPN_BCE, LM2904 (3 units) + LM358 (extends), 74HC00 (5 units),
        /// Conn_01x02, ATtiny85 (no model) and the GND power symbol.
        /// </summary>
        private const string Symbols = """
            (kicad_symbol_lib
            	(version 20231120)
            	(generator "kicad_symbol_editor")
            	(generator_version "8.0")
            	(symbol "R"
            		(pin_numbers (hide yes))
            		(pin_names (offset 0))
            		(exclude_from_sim no)
            		(in_bom yes)
            		(on_board yes)
            		(property "Reference" "R" (at 2.032 0 90) (effects (font (size 1.27 1.27))))
            		(property "Value" "R" (at 0 0 90) (effects (font (size 1.27 1.27))))
            		(property "Footprint" "Resistor_SMD:R_0805_2012Metric" (at -1.778 0 90) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Datasheet" "~" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "Resistor" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_keywords" "R res resistor" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_fp_filters" "R_*" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "R_0_1"
            			(rectangle (start -1.016 -2.54) (end 1.016 2.54) (stroke (width 0.254) (type default)) (fill (type none)))
            		)
            		(symbol "R_1_1"
            			(pin passive line (at 0 3.81 270) (length 1.27)
            				(name "~" (effects (font (size 1.27 1.27))))
            				(number "1" (effects (font (size 1.27 1.27))))
            			)
            			(pin passive line (at 0 -3.81 90) (length 1.27)
            				(name "~" (effects (font (size 1.27 1.27))))
            				(number "2" (effects (font (size 1.27 1.27))))
            			)
            		)
            	)
            	(symbol "C_Polarized" (pin_numbers hide) (pin_names (offset 0.254)) (in_bom yes) (on_board yes)
            		(property "Reference" "C" (id 0) (at 0.635 2.54 0) (effects (font (size 1.27 1.27)) (justify left)))
            		(property "Value" "C_Polarized" (id 1) (at 0.635 -2.54 0) (effects (font (size 1.27 1.27)) (justify left)))
            		(property "Footprint" "" (id 2) (at 0.9652 -3.81 0) (effects (font (size 1.27 1.27)) hide))
            		(property "Datasheet" "~" (id 3) (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
            		(property "ki_keywords" "cap capacitor" (id 4) (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
            		(property "ki_description" "Polarized capacitor" (id 5) (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
            		(property "ki_fp_filters" "CP_*" (id 6) (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
            		(symbol "C_Polarized_0_1"
            			(rectangle (start -2.286 0.508) (end 2.286 1.016) (stroke (width 0) (type default) (color 0 0 0 0)) (fill (type none)))
            			(polyline (pts (xy -1.778 2.286) (xy -0.762 2.286)) (stroke (width 0) (type default) (color 0 0 0 0)) (fill (type none)))
            			(polyline (pts (xy -1.27 2.794) (xy -1.27 1.778)) (stroke (width 0) (type default) (color 0 0 0 0)) (fill (type none)))
            			(rectangle (start 2.286 -0.508) (end -2.286 -1.016) (stroke (width 0) (type default) (color 0 0 0 0)) (fill (type outline)))
            		)
            		(symbol "C_Polarized_1_1"
            			(pin passive line (at 0 3.81 270) (length 2.794) (name "~" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
            			(pin passive line (at 0 -3.81 90) (length 2.794) (name "~" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
            		)
            	)
            	(symbol "LED"
            		(pin_numbers (hide yes))
            		(pin_names (offset 1.016) (hide yes))
            		(exclude_from_sim no)
            		(in_bom yes)
            		(on_board yes)
            		(property "Reference" "D" (at 0 2.54 0) (effects (font (size 1.27 1.27))))
            		(property "Value" "LED" (at 0 -2.54 0) (effects (font (size 1.27 1.27))))
            		(property "Footprint" "LED_THT:LED_D5.0mm" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Datasheet" "~" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "Light emitting diode" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_keywords" "LED diode" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_fp_filters" "LED* LED_SMD:* LED_THT:*" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "LED_0_1"
            			(polyline (pts (xy -1.27 -1.27) (xy -1.27 1.27)) (stroke (width 0.254) (type default)) (fill (type none)))
            			(polyline (pts (xy -1.27 0) (xy 1.27 0)) (stroke (width 0) (type default)) (fill (type none)))
            			(polyline (pts (xy 1.27 -1.27) (xy 1.27 1.27) (xy -1.27 0) (xy 1.27 -1.27)) (stroke (width 0.254) (type default)) (fill (type none)))
            		)
            		(symbol "LED_1_1"
            			(pin passive line (at -3.81 0 0) (length 2.54) (name "K" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
            			(pin passive line (at 3.81 0 180) (length 2.54) (name "A" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
            		)
            	)
            	(symbol "Q_NPN_BCE"
            		(pin_names (offset 0) (hide yes))
            		(exclude_from_sim no)
            		(in_bom yes)
            		(on_board yes)
            		(property "Reference" "Q" (at 5.08 1.27 0) (effects (font (size 1.27 1.27)) (justify left)))
            		(property "Value" "Q_NPN_BCE" (at 5.08 -1.27 0) (effects (font (size 1.27 1.27)) (justify left)))
            		(property "Footprint" "" (at 5.08 2.54 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Datasheet" "~" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "NPN transistor, base/collector/emitter" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_keywords" "transistor NPN" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "Q_NPN_BCE_0_1"
            			(polyline (pts (xy 0.635 0.635) (xy 2.54 2.54)) (stroke (width 0) (type default)) (fill (type none)))
            			(polyline (pts (xy 0.635 -0.635) (xy 2.54 -2.54) (xy 2.54 -2.54)) (stroke (width 0) (type default)) (fill (type none)))
            			(polyline (pts (xy 0.635 1.905) (xy 0.635 -1.905) (xy 0.635 -1.905)) (stroke (width 0.508) (type default)) (fill (type none)))
            			(circle (center 1.27 0) (radius 2.8194) (stroke (width 0.254) (type default)) (fill (type none)))
            		)
            		(symbol "Q_NPN_BCE_1_1"
            			(pin input line (at -5.08 0 0) (length 5.715) (name "B" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
            			(pin passive line (at 2.54 5.08 270) (length 2.54) (name "C" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
            			(pin passive line (at 2.54 -5.08 90) (length 2.54) (name "E" (effects (font (size 1.27 1.27)))) (number "3" (effects (font (size 1.27 1.27)))))
            		)
            	)
            	(symbol "LM2904"
            		(pin_names (offset 0.127))
            		(exclude_from_sim no)
            		(in_bom yes)
            		(on_board yes)
            		(property "Reference" "U" (at 0 5.08 0) (effects (font (size 1.27 1.27)) (justify left)))
            		(property "Value" "LM2904" (at 0 -5.08 0) (effects (font (size 1.27 1.27)) (justify left)))
            		(property "Footprint" "" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Datasheet" "http://www.ti.com/lit/ds/symlink/lm358.pdf" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "Low-Power, Dual Operational Amplifiers, DIP-8/SOIC-8/TO-99-8" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_locked" "" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "ki_keywords" "dual opamp" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_fp_filters" "SOIC*3.9x4.9mm*P1.27mm* DIP*W7.62mm* TO*99* OnSemi*Micro8* TSSOP*3x3mm*P0.65mm*" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "LM2904_1_1"
            			(polyline (pts (xy -5.08 5.08) (xy 5.08 0) (xy -5.08 -5.08) (xy -5.08 5.08)) (stroke (width 0.254) (type default)) (fill (type background)))
            			(pin output line (at 7.62 0 180) (length 2.54) (name "~" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
            			(pin input line (at -7.62 -2.54 0) (length 2.54) (name "-" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
            			(pin input line (at -7.62 2.54 0) (length 2.54) (name "+" (effects (font (size 1.27 1.27)))) (number "3" (effects (font (size 1.27 1.27)))))
            		)
            		(symbol "LM2904_2_1"
            			(polyline (pts (xy -5.08 5.08) (xy 5.08 0) (xy -5.08 -5.08) (xy -5.08 5.08)) (stroke (width 0.254) (type default)) (fill (type background)))
            			(pin input line (at -7.62 2.54 0) (length 2.54) (name "+" (effects (font (size 1.27 1.27)))) (number "5" (effects (font (size 1.27 1.27)))))
            			(pin input line (at -7.62 -2.54 0) (length 2.54) (name "-" (effects (font (size 1.27 1.27)))) (number "6" (effects (font (size 1.27 1.27)))))
            			(pin output line (at 7.62 0 180) (length 2.54) (name "~" (effects (font (size 1.27 1.27)))) (number "7" (effects (font (size 1.27 1.27)))))
            		)
            		(symbol "LM2904_3_1"
            			(pin power_in line (at -2.54 -7.62 90) (length 3.81) (name "V-" (effects (font (size 1.27 1.27)))) (number "4" (effects (font (size 1.27 1.27)))))
            			(pin power_in line (at -2.54 7.62 270) (length 3.81) (name "V+" (effects (font (size 1.27 1.27)))) (number "8" (effects (font (size 1.27 1.27)))))
            		)
            	)
            	(symbol "LM358"
            		(extends "LM2904")
            		(property "Reference" "U" (at 0 5.08 0) (effects (font (size 1.27 1.27)) (justify left)))
            		(property "Value" "LM358" (at 0 -5.08 0) (effects (font (size 1.27 1.27)) (justify left)))
            		(property "Footprint" "Package_DIP:DIP-8_W7.62mm_LongPads" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Datasheet" "http://www.ti.com/lit/ds/symlink/lm2904-n.pdf" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "Low-Power, Dual Operational Amplifiers, DIP-8/SOIC-8/TO-99-8/TSSOP-8/VSSOP-8" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            	)
            	(symbol "74HC00"
            		(pin_names (offset 1.016))
            		(exclude_from_sim no)
            		(in_bom yes)
            		(on_board yes)
            		(property "Reference" "U" (at 0 1.27 0) (effects (font (size 1.27 1.27))))
            		(property "Value" "74HC00" (at 0 -1.27 0) (effects (font (size 1.27 1.27))))
            		(property "Footprint" "" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Datasheet" "http://www.ti.com/lit/gpn/sn74hc00" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "quad 2-input NAND gate" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_keywords" "HCMOS nand 2-input" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "74HC00_1_1"
            			(pin input line (at -7.62 2.54 0) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
            			(pin input line (at -7.62 -2.54 0) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
            			(pin output inverted (at 7.62 0 180) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "3" (effects (font (size 1.27 1.27)))))
            		)
            		(symbol "74HC00_2_1"
            			(pin input line (at -7.62 2.54 0) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "4" (effects (font (size 1.27 1.27)))))
            			(pin input line (at -7.62 -2.54 0) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "5" (effects (font (size 1.27 1.27)))))
            			(pin output inverted (at 7.62 0 180) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "6" (effects (font (size 1.27 1.27)))))
            		)
            		(symbol "74HC00_3_1"
            			(pin input line (at -7.62 2.54 0) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "9" (effects (font (size 1.27 1.27)))))
            			(pin input line (at -7.62 -2.54 0) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "10" (effects (font (size 1.27 1.27)))))
            			(pin output inverted (at 7.62 0 180) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "8" (effects (font (size 1.27 1.27)))))
            		)
            		(symbol "74HC00_4_1"
            			(pin input line (at -7.62 2.54 0) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "12" (effects (font (size 1.27 1.27)))))
            			(pin input line (at -7.62 -2.54 0) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "13" (effects (font (size 1.27 1.27)))))
            			(pin output inverted (at 7.62 0 180) (length 3.81) (name "~" (effects (font (size 1.27 1.27)))) (number "11" (effects (font (size 1.27 1.27)))))
            		)
            		(symbol "74HC00_5_0"
            			(pin power_in line (at 0 12.7 270) (length 5.08) (name "VCC" (effects (font (size 1.27 1.27)))) (number "14" (effects (font (size 1.27 1.27)))))
            			(pin power_in line (at 0 -12.7 90) (length 5.08) (name "GND" (effects (font (size 1.27 1.27)))) (number "7" (effects (font (size 1.27 1.27)))))
            		)
            	)
            	(symbol "Conn_01x02"
            		(pin_names (offset 1.016) (hide yes))
            		(exclude_from_sim no)
            		(in_bom yes)
            		(on_board yes)
            		(property "Reference" "J" (at 0 2.54 0) (effects (font (size 1.27 1.27))))
            		(property "Value" "Conn_01x02" (at 0 -5.08 0) (effects (font (size 1.27 1.27))))
            		(property "Footprint" "Connector_JST:JST_XH_B2B-XH-AM_1x02_P2.50mm_Vertical" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Datasheet" "~" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "Generic connector, single row, 01x02, script generated" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_keywords" "connector" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_fp_filters" "Connector*:*_1x??_*" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "Conn_01x02_1_1"
            			(rectangle (start -1.27 1.27) (end 1.27 -3.81) (stroke (width 0.254) (type default)) (fill (type background)))
            			(pin passive line (at -5.08 0 0) (length 3.81) (name "Pin_1" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
            			(pin passive line (at -5.08 -2.54 0) (length 3.81) (name "Pin_2" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
            		)
            	)
            	(symbol "ATtiny85-20P"
            		(exclude_from_sim no)
            		(in_bom yes)
            		(on_board yes)
            		(property "Reference" "U" (at -12.7 13.97 0) (effects (font (size 1.27 1.27)) (justify left bottom)))
            		(property "Value" "ATtiny85-20P" (at 2.54 -13.97 0) (effects (font (size 1.27 1.27)) (justify left top)))
            		(property "Footprint" "Package_DIP:DIP-8_W7.62mm_LongPads" (at 0 0 0) (effects (font (size 1.27 1.27) italic) (hide yes)))
            		(property "Datasheet" "http://ww1.microchip.com/downloads/en/DeviceDoc/atmel-2586-avr-8-bit-microcontroller-attiny25-attiny45-attiny85_datasheet.pdf" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "20MHz, 8kB Flash, 512B SRAM, 512B EEPROM, debugWIRE, DIP-8" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_keywords" "AVR 8bit Microcontroller tinyAVR" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "ATtiny85-20P_0_1"
            			(rectangle (start -12.7 -12.7) (end 12.7 12.7) (stroke (width 0.254) (type default)) (fill (type background)))
            		)
            		(symbol "ATtiny85-20P_1_1"
            			(pin bidirectional line (at 15.24 -5.08 180) (length 2.54) (name "~{RESET}/PB5" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
            			(pin bidirectional line (at 15.24 2.54 180) (length 2.54) (name "XTAL1/PB3" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))
            			(pin bidirectional line (at 15.24 0 180) (length 2.54) (name "XTAL2/PB4" (effects (font (size 1.27 1.27)))) (number "3" (effects (font (size 1.27 1.27)))))
            			(pin power_in line (at 0 -15.24 90) (length 2.54) (name "GND" (effects (font (size 1.27 1.27)))) (number "4" (effects (font (size 1.27 1.27)))))
            			(pin bidirectional line (at 15.24 10.16 180) (length 2.54) (name "AREF/PB0" (effects (font (size 1.27 1.27)))) (number "5" (effects (font (size 1.27 1.27)))))
            			(pin bidirectional line (at 15.24 7.62 180) (length 2.54) (name "PB1" (effects (font (size 1.27 1.27)))) (number "6" (effects (font (size 1.27 1.27)))))
            			(pin bidirectional line (at 15.24 5.08 180) (length 2.54) (name "PB2" (effects (font (size 1.27 1.27)))) (number "7" (effects (font (size 1.27 1.27)))))
            			(pin power_in line (at 0 15.24 270) (length 2.54) (name "VCC" (effects (font (size 1.27 1.27)))) (number "8" (effects (font (size 1.27 1.27)))))
            		)
            	)
            	(symbol "GND"
            		(power)
            		(pin_numbers (hide yes))
            		(pin_names (offset 0) (hide yes))
            		(exclude_from_sim no)
            		(in_bom yes)
            		(on_board yes)
            		(property "Reference" "#PWR" (at 0 -6.35 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Value" "GND" (at 0 -3.81 0) (effects (font (size 1.27 1.27))))
            		(property "Footprint" "" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Datasheet" "" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "Description" "Power symbol creates a global label with name \"GND\" , ground" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(property "ki_keywords" "global power" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "GND_0_1"
            			(polyline (pts (xy 0 0) (xy 0 -1.27) (xy 1.27 -1.27) (xy 0 -2.54) (xy -1.27 -1.27) (xy 0 -1.27)) (stroke (width 0) (type default)) (fill (type none)))
            		)
            		(symbol "GND_1_1"
            			(pin power_in line (at 0 0 270) (length 0) (name "GND" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
            		)
            	)
            )
            """;

        private static List<KiCadSymbol> LoadSymbols() => KiCadLibraryImport.ImportSymbolLibrary(Symbols, "Test");

        private static KiCadSymbol Sym(List<KiCadSymbol> all, string name) => all.Single(s => s.Name == name);

        private static PartLibrary FreshLibrary()
        {
            var lib = new PartLibrary();
            foreach (var p in PartLibrary.Default.Parts) lib.Add(p);
            return lib;
        }

        private static void AssertPad(Footprint fp, string name, double x, double y, double w, double h, PadShape shape, double drill, CopperLayer layer = CopperLayer.Top)
        {
            var pad = fp.FindPad(name);
            Assert.True(pad != null, $"{fp.Id}: pad {name} missing");
            Assert.Equal(x, pad!.X, 6);
            Assert.Equal(y, pad.Y, 6);
            Assert.Equal(w, pad.Width, 6);
            Assert.Equal(h, pad.Height, 6);
            Assert.Equal(shape, pad.Shape);
            Assert.Equal(drill, pad.Drill, 6);
            if (drill == 0) Assert.Equal(layer, pad.Layer);
        }

        private static void AssertCourtyard(Footprint fp, double minX, double minY, double maxX, double maxY)
        {
            Assert.Equal(minX, fp.CourtyardMinX, 6);
            Assert.Equal(minY, fp.CourtyardMinY, 6);
            Assert.Equal(maxX, fp.CourtyardMaxX, 6);
            Assert.Equal(maxY, fp.CourtyardMaxY, 6);
        }

        private static Dictionary<string, string> PinMap(CustomPart part) =>
            part.Parameters["pinmap"].Split(',').Select(e => e.Split(':')).ToDictionary(e => e[0], e => e[1]);

        // ================================================================== s-expressions

        [Fact]
        public void SExpressionParserHandlesEscapesNumbersAndSloppyInput()
        {
            var root = SExpression.Parse("﻿# legacy header line\n(lib (name \"a \\\"b\\\" \\\\ c\\nd\") (n -1.5e1 .5 +3) bare_token (empty) (x \"\\x41\\101\")");
            Assert.Equal("lib", root.Head);
            Assert.Equal("a \"b\" \\ c\nd", root.ChildText("name"));
            Assert.Equal(new[] { -15.0, 0.5, 3.0 }, root.Child("n")!.Numbers());
            Assert.Contains(root.Items, i => i.Atom == "bare_token" && !i.IsQuoted);
            Assert.Equal("AA", root.ChildText("x"));   // \x41 and octal \101
            Assert.NotNull(root.Child("empty"));       // unclosed root list closed at end of text

            var tolerant = SExpression.ParseAll("(a (b x)) ) ) (c) stray");
            Assert.Equal(2, tolerant.Count(t => t.IsList));
            Assert.Equal("open", SExpression.Parse("(a \"open").Text(1));   // unterminated string takes the rest
            Assert.True(SExpression.Parse("(pin (hide yes))").Flag("hide"));
            Assert.True(SExpression.Parse("(pin hide)").Flag("hide"));
            Assert.False(SExpression.Parse("(pin (hide no))").Flag("hide"));
            Assert.Throws<FormatException>(() => SExpression.Parse("just atoms"));
        }

        // ================================================================== footprints

        [Fact]
        public void DipWithOvalPadsImportsPadsCourtyardSilkAndModel()
        {
            var fp = KiCadLibraryImport.ImportFootprint(Dip8);
            Assert.Equal("DIP-8_W7.62mm_LongPads", fp.Id);
            Assert.StartsWith("8-lead though-hole", fp.Description);
            Assert.Equal(8, fp.Pads.Count);
            AssertPad(fp, "1", 0, 0, 2.4, 1.6, PadShape.Rect, 0.8);
            AssertPad(fp, "2", 0, 2.54, 2.4, 1.6, PadShape.Oval, 0.8);
            AssertPad(fp, "4", 0, 7.62, 2.4, 1.6, PadShape.Oval, 0.8);
            AssertPad(fp, "5", 7.62, 7.62, 2.4, 1.6, PadShape.Oval, 0.8);
            AssertPad(fp, "8", 7.62, 0, 2.4, 1.6, PadShape.Oval, 0.8);
            Assert.All(fp.Pads, p => Assert.True(p.IsThroughHole));

            AssertCourtyard(fp, -1.45, -1.55, 9.1, 9.15);
            // 5 silk lines + the pin-1 notch arc (180° tessellated into 12 chords); F.Fab lines are ignored.
            Assert.Equal(5 + 12, fp.Silkscreen.Count);
            var arc = fp.Silkscreen.Skip(5).ToList();
            Assert.Equal(4.81, arc[0].X1, 6);
            Assert.Equal(2.81, arc[11].X2, 6);
            Assert.True(arc.Max(s => Math.Max(s.Y1, s.Y2)) > -0.34, "the notch bulges towards +Y (into the body)");
            Assert.Equal(4.5, fp.Height, 6);

            var info = KiCadLibraryImport.GetFootprintInfo(fp)!;
            Assert.True(info.CourtyardFromFile);
            Assert.Equal("${KICAD8_3DMODEL_DIR}/Package_DIP.3dshapes/DIP-8_W7.62mm.wrl", info.Models.Single().Path);
            Assert.Equal(new[] { "through_hole" }, info.Attributes);
            Assert.Equal("20240108", info.FileVersion);
            Assert.False(info.IsLegacyModule);
        }

        [Fact]
        public void Sot23SmdRoundRectPadsAndPolygonCourtyard()
        {
            var fp = KiCadLibraryImport.ImportFootprint(Sot23);
            Assert.Equal(3, fp.Pads.Count);
            AssertPad(fp, "1", -0.9375, -0.95, 1.475, 0.6, PadShape.RoundRect, 0);
            AssertPad(fp, "2", -0.9375, 0.95, 1.475, 0.6, PadShape.RoundRect, 0);
            AssertPad(fp, "3", 0.9375, 0, 1.475, 0.6, PadShape.RoundRect, 0);
            AssertCourtyard(fp, -1.92, -1.7, 1.92, 1.7);
            Assert.Equal(4 + 3, fp.Silkscreen.Count);   // 4 lines + the closed pin-1 triangle
            Assert.Equal(1.1, fp.Height, 6);
        }

        [Fact]
        public void Chip0805RoundRectPadsAndRectCourtyard()
        {
            var fp = KiCadLibraryImport.ImportFootprint(R0805);
            AssertPad(fp, "1", -0.9125, 0, 1.025, 1.4, PadShape.RoundRect, 0);
            AssertPad(fp, "2", 0.9125, 0, 1.025, 1.4, PadShape.RoundRect, 0);
            AssertCourtyard(fp, -1.68, -0.95, 1.68, 0.95);
            Assert.Equal(2, fp.Silkscreen.Count);
            Assert.Equal(0.6, fp.Height, 6);
        }

        [Fact]
        public void RotatedTrapezoidCustomAndBottomPadsFollowKiCadConventions()
        {
            var fp = KiCadLibraryImport.ImportFootprint(Rotated);
            Assert.Equal(6, fp.Pads.Count);   // the paste-only aperture has no copper and is skipped
            AssertPad(fp, "1", 0, 0, 1, 2, PadShape.Rect, 0);                 // 90°: width and height swap
            AssertPad(fp, "2", 5, 0, 1.5, 3, PadShape.Oval, 2.0);             // 270° + oval drill -> larger slot dimension
            AssertPad(fp, "3", 0, 5, 2.4, 1, PadShape.Rect, 0);               // trapezoid -> bounding rect
            // Custom pad: primitive spans x -0.5..2.5 locally; KiCad's 90° turns +X into -Y (Y down).
            AssertPad(fp, "4", 5, 4, 1, 3, PadShape.Rect, 0);
            AssertPad(fp, "5", 10, 0, 2 * Math.Sqrt(2), 2 * Math.Sqrt(2), PadShape.Rect, 0);   // 45° -> bounding box
            AssertPad(fp, "6", 10, 5, 1.2, 1.2, PadShape.Circle, 0, CopperLayer.Bottom);

            var silk = fp.SilkCircles.Single();
            Assert.Equal((5.0, 5.0, 1.5), (silk.X, silk.Y, Math.Round(silk.R, 9)));
            // No courtyard graphics: pad outline + 0.25 mm.
            AssertCourtyard(fp, -1.2 - 0.25, -1.5 - 0.25, 10 + Math.Sqrt(2) + 0.25, 5.6 + 0.25);
            var info = KiCadLibraryImport.GetFootprintInfo(fp)!;
            Assert.False(info.CourtyardFromFile);
            Assert.Contains(info.Warnings, w => w.Contains("bakır katmanı yok"));
            Assert.Contains(info.Warnings, w => w.Contains("45"));
        }

        [Fact]
        public void NonPlatedHoleAndModelReferenceAreKept()
        {
            var fp = KiCadLibraryImport.ImportFootprint(JstXh, "Connector_JST");
            Assert.Equal(3, fp.Pads.Count);
            AssertPad(fp, "1", 0, 0, 1.7, 1.95, PadShape.RoundRect, 0.95);
            AssertPad(fp, "2", 2.5, 0, 1.7, 1.95, PadShape.Oval, 0.95);
            var hole = fp.Pads.Single(p => p.Name.Length == 0);
            Assert.Equal((1.25, 2.0, 1.2), (hole.X, hole.Y, hole.Drill));
            AssertCourtyard(fp, -2.95, -2.85, 5.45, 3.9);
            Assert.Equal(4, fp.Silkscreen.Count);
            Assert.Equal(7.0, fp.Height, 6);

            var info = KiCadLibraryImport.GetFootprintInfo(fp)!;
            Assert.Equal(1, info.NonPlatedHoles);
            Assert.Equal("Connector_JST", info.Library);
            var model = info.Models.Single();
            Assert.EndsWith("JST_XH_B2B-XH-AM_1x02_P2.50mm_Vertical.wrl", model.Path);
            Assert.Equal((1.0, 1.0, 1.0), (model.ScaleX, model.ScaleY, model.ScaleZ));
        }

        [Fact]
        public void LegacyModuleKeywordWithBareAtomsAndOldArcs()
        {
            var fp = KiCadLibraryImport.ImportFootprint(LegacyMountingHole);
            Assert.Equal("MountingHole_3.2mm_M3", fp.Id);
            var hole = fp.Pads.Single();
            Assert.Equal(3.2, hole.Drill, 6);
            Assert.Equal(3.2, hole.Width, 6);
            AssertCourtyard(fp, -3.45, -3.45, 3.45, 3.45);   // circle on F.CrtYd; Cmts.User ignored
            Assert.Equal(0.0, fp.Height, 6);
            // KiCad 5 arc: centre (0,0), start (2,0), +90° -> ends at (0,2) in the Y-down frame.
            var last = fp.Silkscreen.Last();
            Assert.Equal(2.0, fp.Silkscreen.First().X1, 6);
            Assert.Equal(0.0, last.X2, 6);
            Assert.Equal(2.0, last.Y2, 6);
            var info = KiCadLibraryImport.GetFootprintInfo(fp)!;
            Assert.True(info.IsLegacyModule);
            Assert.Equal(1, info.NonPlatedHoles);
            Assert.Contains("virtual", info.Attributes);
        }

        [Fact]
        public void NotAFootprintGivesAClearError()
        {
            var ex = Assert.Throws<FormatException>(() => KiCadLibraryImport.ImportFootprint("(kicad_pcb (version 20240108))"));
            Assert.Contains("kicad_pcb", ex.Message);
        }

        [Fact]
        public void BuiltInFootprintsRoundTripThroughKiCadExporter()
        {
            var ids = FootprintLibrary.KnownIds.ToList();
            var samples = new[] { Dip8, Sot23, R0805, JstXh }.Select(t => KiCadLibraryImport.ImportFootprint(t)).ToList();
            foreach (var original in ids.Select(id => FootprintLibrary.Get(id)!).Concat(samples))
            {
                var copy = KiCadLibraryImport.ImportFootprint(KiCadExporter.ExportFootprint(original));
                Assert.Equal(original.Id, copy.Id);
                Assert.Equal(original.Description, copy.Description);
                Assert.Equal(original.Pads.Count, copy.Pads.Count);
                for (int i = 0; i < original.Pads.Count; i++)
                {
                    Pad a = original.Pads[i], b = copy.Pads[i];
                    string where = $"{original.Id} pad {a.Name}";
                    Assert.True(a.Name == b.Name, where);
                    Assert.True(Math.Abs(a.X - b.X) < Tol && Math.Abs(a.Y - b.Y) < Tol, where + " position");
                    Assert.True(Math.Abs(a.Width - b.Width) < Tol && Math.Abs(a.Height - b.Height) < Tol, where + " size");
                    Assert.True(a.Shape == b.Shape, where + " shape");
                    Assert.True(Math.Abs(a.Drill - b.Drill) < Tol, where + " drill");
                    if (!a.IsThroughHole) Assert.True(a.Layer == b.Layer, where + " layer");
                }

                Assert.True(Math.Abs(original.CourtyardMinX - copy.CourtyardMinX) < Tol && Math.Abs(original.CourtyardMaxY - copy.CourtyardMaxY) < Tol, original.Id + " courtyard");
                if (original.Silkscreen.Count + original.SilkCircles.Count > 0)
                {
                    Assert.Equal(original.Silkscreen.Count, copy.Silkscreen.Count);
                    Assert.Equal(original.SilkCircles.Count, copy.SilkCircles.Count);
                }
            }
        }

        [Fact]
        public void PrettyFolderUsesFileNamesAndReportsBadFiles()
        {
            string pretty = Path.Combine(_dir, "Mine.pretty");
            Directory.CreateDirectory(pretty);
            File.WriteAllText(Path.Combine(pretty, "SOT-23.kicad_mod"), Sot23);
            File.WriteAllText(Path.Combine(pretty, "Renamed_0805.kicad_mod"), R0805);
            File.WriteAllText(Path.Combine(pretty, "Broken.kicad_mod"), "(kicad_symbol_lib)");
            var errors = new List<string>();
            var list = KiCadLibraryImport.ImportFootprintLibrary(pretty, errors);
            Assert.Equal(new[] { "Renamed_0805", "SOT-23" }, list.Select(f => f.Id).OrderBy(s => s, StringComparer.Ordinal));
            Assert.Contains(errors, e => e.Contains("Broken.kicad_mod"));
            Assert.Equal("Mine", KiCadLibraryImport.GetFootprintInfo(list[0])!.Library);
            Assert.Equal(2, KiCadLibraryImport.ImportFootprintLibrary(pretty).Count());
        }

        // ================================================================== symbols

        [Fact]
        public void SymbolLibraryParsesPropertiesUnitsPinsAndExtends()
        {
            var all = LoadSymbols();
            Assert.Equal(10, all.Count);

            var r = Sym(all, "R");
            Assert.Equal("R", r.Reference);
            Assert.Equal("Resistor_SMD:R_0805_2012Metric", r.FootprintRef);
            Assert.Equal("Resistor", r.Description);
            Assert.Equal("R res resistor", r.Keywords);
            Assert.Equal(new[] { "R_*" }, r.FootprintFilters);
            Assert.True(r.PinNumbersHidden);
            Assert.Equal(2, r.Pins.Count);
            var p1 = r.Pins.Single(p => p.Number == "1");
            Assert.Equal((0.0, 3.81, 270.0, 1.27, "passive", 1), (p1.X, p1.Y, p1.Angle, p1.Length, p1.ElectricalType, p1.Unit));
            Assert.Equal(string.Empty, p1.CleanName);
            Assert.True(r.HasBounds);
            Assert.Equal((-1.016, -3.81, 1.016, 3.81), (r.MinX, r.MinY, r.MaxX, r.MaxY));

            var cp = Sym(all, "C_Polarized");   // KiCad 6 syntax
            Assert.Equal("Polarized capacitor", cp.Description);
            Assert.True(cp.PinNumbersHidden);
            Assert.Equal(2, cp.Pins.Count);

            var lm2904 = Sym(all, "LM2904");
            Assert.Equal(3, lm2904.UnitCount);
            Assert.Equal(8, lm2904.Pins.Count);
            Assert.Equal(new[] { "1", "2", "3", "4", "8" }, lm2904.PinsOfUnit(1).Concat(lm2904.PinsOfUnit(3)).Select(p => p.Number).Distinct().OrderBy(n => n));

            var lm358 = Sym(all, "LM358");
            Assert.Equal("LM2904", lm358.Extends);
            Assert.Equal(3, lm358.UnitCount);
            Assert.Equal(8, lm358.Pins.Count);
            Assert.Equal("LM358", lm358.Value);
            Assert.Equal("Package_DIP:DIP-8_W7.62mm_LongPads", lm358.FootprintRef);
            Assert.Equal("dual opamp", lm358.Keywords);   // inherited
            Assert.Contains("TSSOP-8", lm358.Description);  // own
            Assert.True(lm358.HasBounds);
            Assert.Equal("Test:LM358", lm358.FullName);

            var hc00 = Sym(all, "74HC00");
            Assert.Equal(5, hc00.UnitCount);
            Assert.Equal(14, hc00.Pins.Count);
            Assert.Equal(0, hc00.Pins.Single(p => p.Number == "14").BodyStyle);

            Assert.Equal("RESET/PB5", Sym(all, "ATtiny85-20P").Pins.Single(p => p.Number == "1").CleanName);
            var gnd = Sym(all, "GND");
            Assert.True(gnd.IsPower);
            Assert.Contains("\"GND\"", gnd.Description);
        }

        [Fact]
        public void MissingParentIsReportedNotFatal()
        {
            var list = KiCadLibraryImport.ImportSymbolLibrary("(kicad_symbol_lib (version 20231120) (symbol \"X\" (extends \"Nope\") (property \"Reference\" \"U\")))");
            Assert.Contains(list.Single().Warnings, w => w.Contains("Nope"));
        }

        // ================================================================== mapping

        [Fact]
        public void PassivesMapOntoModelsWithValuesAndPolarity()
        {
            var all = LoadSymbols();
            var lib = PartLibrary.Default;

            var r = Sym(all, "R");
            var part = KiCadLibraryImport.ToCustomPart(r, null, lib, out string report)!;
            Assert.Equal("resistor", part.BasePartId);
            Assert.Equal(new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" }, PinMap(part));
            Assert.False(part.Parameters.ContainsKey("resistance"));
            Assert.Contains("bir sayı değil", report);

            r.Properties["Value"] = "4k7";
            var fp = KiCadLibraryImport.ImportFootprint(R0805, "Resistor_SMD");
            part = KiCadLibraryImport.ToCustomPart(r, fp, lib, out report)!;
            Assert.Equal("4k7", part.Parameters["resistance"]);
            Assert.Equal(4700, SiValue.Parse(part.Parameters["resistance"], 0), 9);
            Assert.Equal("R_0805_2012Metric", part.Parameters["footprint"]);
            Assert.Equal("R_0805_2012Metric", part.Footprint!.Id);
            Assert.Equal(2, part.Footprint.Pads.Count);
            Assert.Equal("kicad-test-r", part.Id);
            Assert.Equal("R (4k7)", part.Name);
            Assert.DoesNotContain("uyarı", report);

            var cp = Sym(all, "C_Polarized");
            cp.Properties["Value"] = "100u";
            part = KiCadLibraryImport.ToCustomPart(cp, null, lib, out report)!;
            Assert.Equal("electrolytic", part.BasePartId);
            Assert.Equal(new Dictionary<string, string> { ["+"] = "1", ["-"] = "2" }, PinMap(part));
            Assert.Equal("100u", part.Parameters["capacitance"]);
            Assert.Contains("bacak 1 = +", report);
            Assert.Equal("Polarized capacitor", part.Description);

            var led = Sym(all, "LED");
            part = KiCadLibraryImport.ToCustomPart(led, null, lib, out report)!;
            Assert.Equal("led", part.BasePartId);
            Assert.Equal(new Dictionary<string, string> { ["A"] = "2", ["K"] = "1" }, PinMap(part));
            Assert.Contains("LED_THT:LED_D5.0mm", report);   // footprint not supplied: model default used
            led.Properties["ki_keywords"] = "LED diode green";
            part = KiCadLibraryImport.ToCustomPart(led, null, lib, out _)!;
            Assert.Equal("green", part.Parameters["color"]);
            Assert.Equal("2.2", part.Parameters["vf"]);
        }

        [Fact]
        public void SemiconductorsAndIcsMapPinsByNameAliasAndType()
        {
            var all = LoadSymbols();
            var lib = PartLibrary.Default;

            var npn = KiCadLibraryImport.ToCustomPart(Sym(all, "Q_NPN_BCE"), null, lib, out string report)!;
            Assert.Equal("npn", npn.BasePartId);
            Assert.Equal(new Dictionary<string, string> { ["B"] = "1", ["C"] = "2", ["E"] = "3" }, PinMap(npn));
            Assert.Contains("NPN sözcüğü", report);

            // Pin names B/C/E alone decide the model family; the part number picks the polarity preset.
            var q = Sym(all, "Q_NPN_BCE");
            q.Name = "Q_BCE";
            q.Properties["ki_keywords"] = "transistor";
            q.Properties["Description"] = "transistor";
            q.Properties["Value"] = "2N3906";
            var pnp = KiCadLibraryImport.ToCustomPart(q, null, lib, out report)!;
            Assert.Equal("pnp", pnp.BasePartId);
            Assert.Equal("2N3906", pnp.Parameters["model"]);
            Assert.Equal("180.7", pnp.Parameters["bf"]);
            Assert.Contains("2n3906", report);

            var fp = KiCadLibraryImport.ImportFootprint(Dip8, "Package_DIP");
            var opamp = KiCadLibraryImport.ToCustomPart(Sym(all, "LM358"), fp, lib, out report)!;
            Assert.Equal("opamp", opamp.BasePartId);
            Assert.Equal(new Dictionary<string, string> { ["IN-"] = "2", ["IN+"] = "3", ["OUT"] = "1", ["V+"] = "8", ["V-"] = "4" }, PinMap(opamp));
            Assert.Contains("'lm358' hazır ayarıyla", report);
            Assert.Contains("birim 2 modellenmedi", report);
            Assert.Equal("DIP-8_W7.62mm_LongPads", opamp.Parameters["footprint"]);
            Assert.DoesNotContain("uyarı", report);

            var parent = KiCadLibraryImport.ToCustomPart(Sym(all, "LM2904"), null, lib, out _)!;
            Assert.Equal("opamp", parent.BasePartId);
            Assert.Equal("LM2904", parent.Parameters["model"]);

            var nand = KiCadLibraryImport.ToCustomPart(Sym(all, "74HC00"), null, lib, out report)!;
            Assert.Equal("gate2", nand.BasePartId);
            Assert.Equal("Nand", nand.Parameters["function"]);
            Assert.Equal(new Dictionary<string, string> { ["A"] = "1", ["B"] = "2", ["Y"] = "3", ["VCC"] = "14", ["GND"] = "7" }, PinMap(nand));
        }

        [Fact]
        public void ConnectorsGenericPartsAndUnmappableSymbols()
        {
            var all = LoadSymbols();
            var lib = PartLibrary.Default;
            var jst = KiCadLibraryImport.ImportFootprint(JstXh, "Connector_JST");

            var conn = KiCadLibraryImport.ToCustomPart(Sym(all, "Conn_01x02"), jst, lib, out string report)!;
            Assert.Equal("header-2", conn.BasePartId);
            Assert.Equal(new Dictionary<string, string> { ["1"] = "1", ["2"] = "2" }, PinMap(conn));
            Assert.StartsWith("Test:Conn_01x02: Mapped", report);
            Assert.Equal(3, conn.Footprint!.Pads.Count);   // includes the non-plated boss

            var mcu = KiCadLibraryImport.ToCustomPart(Sym(all, "ATtiny85-20P"), null, lib, out report)!;
            Assert.Equal("header-8", mcu.BasePartId);
            Assert.StartsWith("Test:ATtiny85-20P: Generic", report);
            Assert.Contains("konnektör gibi", report);
            Assert.Contains("bacak 4 = KiCad bacağı 4 (GND)", report);

            Assert.Null(KiCadLibraryImport.ToCustomPart(Sym(all, "GND"), null, lib, out report));
            Assert.Contains("güç sembolü", report);

            var wide = KiCadLibraryImport.ImportSymbolLibrary("(kicad_symbol_lib (symbol \"Conn_01x10\" (property \"Reference\" \"J\") (symbol \"Conn_01x10_1_1\" "
                + string.Concat(Enumerable.Range(1, 10).Select(i => $"(pin passive line (at 0 {-2.54 * i} 0) (length 3.81) (name \"Pin_{i}\") (number \"{i}\"))"))
                + ")))").Single();
            Assert.Null(KiCadLibraryImport.ToCustomPart(wide, null, lib, out report));
            Assert.Contains("10 bacaklı genel parça yok", report);
        }

        // ================================================================== whole libraries + simulation

        private string WriteLibraries()
        {
            string root = Path.Combine(_dir, "kicad");
            Directory.CreateDirectory(Path.Combine(root, "Package_DIP.pretty"));
            Directory.CreateDirectory(Path.Combine(root, "Resistor_SMD.pretty"));
            Directory.CreateDirectory(Path.Combine(root, "Connector_JST.pretty"));
            File.WriteAllText(Path.Combine(root, "Package_DIP.pretty", "DIP-8_W7.62mm_LongPads.kicad_mod"), Dip8);
            File.WriteAllText(Path.Combine(root, "Resistor_SMD.pretty", "R_0805_2012Metric.kicad_mod"), R0805);
            File.WriteAllText(Path.Combine(root, "Connector_JST.pretty", "JST_XH_B2B-XH-AM_1x02_P2.50mm_Vertical.kicad_mod"), JstXh);
            File.WriteAllText(Path.Combine(root, "Test.kicad_sym"), Symbols);
            return root;
        }

        [Fact]
        public void ImportLibrariesPairsSymbolsWithFootprintsAndSavesAsRwlib()
        {
            string root = WriteLibraries();
            var lib = FreshLibrary();
            var file = KiCadLibraryImport.ImportLibraries(new[]
            {
                Path.Combine(root, "Test.kicad_sym"),
                Path.Combine(root, "Package_DIP.pretty"),
                Path.Combine(root, "Resistor_SMD.pretty"),
                Path.Combine(root, "Connector_JST.pretty", "JST_XH_B2B-XH-AM_1x02_P2.50mm_Vertical.kicad_mod"),
                Path.Combine(root, "missing.kicad_sym")
            }, lib, out var report);

            Assert.Equal(3, report.Footprints.Count);
            Assert.Equal(10, report.Symbols.Count);
            Assert.Contains(report.Errors, e => e.Contains("missing.kicad_sym"));
            Assert.Equal(KiCadImportStatus.Skipped, report.Entries.Single(e => e.Symbol == "Test:GND").Status);
            Assert.Equal(KiCadImportStatus.Generic, report.Entries.Single(e => e.Symbol == "Test:ATtiny85-20P").Status);
            Assert.Equal(8, report.Count(KiCadImportStatus.Mapped));
            Assert.Equal(9, file.Parts.Count);

            string Fp(string symbol) => report.Entries.Single(e => e.Symbol == "Test:" + symbol).FootprintId!;
            Assert.Equal("R_0805_2012Metric", Fp("R"));
            Assert.Equal("DIP-8_W7.62mm_LongPads", Fp("LM358"));
            Assert.Equal("DIP-8_W7.62mm_LongPads", Fp("ATtiny85-20P"));
            Assert.Equal("JST_XH_B2B-XH-AM_1x02_P2.50mm_Vertical", Fp("Conn_01x02"));
            Assert.Null(report.Entries.Single(e => e.Symbol == "Test:LED").FootprintId);
            Assert.Contains("LED_THT:LED_D5.0mm", report.Entries.Single(e => e.Symbol == "Test:LED").Details);
            Assert.Contains("Test:LM358 [Mapped] -> opamp", report.ToString());

            // Same folder scanned as a whole gives the same result.
            var again = KiCadLibraryImport.ImportLibraries(new[] { root }, lib, out var report2);
            Assert.Equal(file.Parts.Select(p => p.Id), again.Parts.Select(p => p.Id));
            Assert.Equal(3, report2.Footprints.Count);

            // .rwlib round trip and registration.
            string path = Path.Combine(_dir, "kicad.rwlib");
            file.Save(path);
            var loaded = PartLibraryFile.Load(path);
            Assert.Equal(file.Parts.Count, loaded.Parts.Count);
            Assert.Equal(9, KiCadLibraryImport.Register(loaded, report, lib));
            var opamp = lib.FindPreset("kicad-test-lm358")!;
            Assert.Equal("opamp", opamp.PartId);
            var def = lib.Get("opamp");
            var dip = FootprintLibrary.Get(opamp.Parameters["footprint"])!;
            Assert.Equal(17, dip.Silkscreen.Count);   // full-fidelity footprint re-registered after the parts
            foreach (var pin in def.Pins) Assert.NotNull(dip.FindPad(def.ResolvePad(pin, opamp.Parameters)));
            Assert.Equal("8", def.ResolvePad(def.FindPin("V+")!, opamp.Parameters));
        }

        [Fact]
        public void BuiltInFootprintNamesAreNotShadowed()
        {
            string pretty = Path.Combine(_dir, "Clash.pretty");
            Directory.CreateDirectory(pretty);
            File.WriteAllText(Path.Combine(pretty, "SOT23.kicad_mod"), Sot23.Replace("\"SOT-23\"", "\"SOT23\""));
            KiCadLibraryImport.ImportLibraries(new[] { pretty }, FreshLibrary(), out var report);
            Assert.Equal("Clash_SOT23", report.Footprints.Single().Id);
            Assert.Contains(report.Warnings, w => w.Contains("yerleşik"));
            Assert.Equal("SOT-23", FootprintLibrary.Get("SOT23")!.Description);
        }

        private static GridPoint Tip(PartLibrary lib, SchematicPart part, string pin)
        {
            var p = lib.Get(part.PartId).FindPin(pin)!;
            return part.Transform(p.X, p.Y);
        }

        [Fact]
        public void ImportedResistorLedAndNpnSimulateInACircuit()
        {
            var all = LoadSymbols();
            var lib = FreshLibrary();
            var r = Sym(all, "R");
            r.Properties["Value"] = "330";
            var parts = new[]
            {
                KiCadLibraryImport.ToCustomPart(r, KiCadLibraryImport.ImportFootprint(R0805, "Resistor_SMD"), lib, out _)!,
                KiCadLibraryImport.ToCustomPart(Sym(all, "LED"), null, lib, out _)!,
                KiCadLibraryImport.ToCustomPart(Sym(all, "Q_NPN_BCE"), null, lib, out _)!
            };
            foreach (var p in parts) Assert.True(p.Register(lib));

            // 5 V -> R1 330 Ω -> LED -> NPN collector; base through R2 (same part, 10 kΩ override) from 5 V.
            var sch = new Schematic { Title = "KiCad import" };
            var v1 = sch.AddPart(lib, "dc-source", new GridPoint(0, 0));
            var r1 = sch.AddPart(lib, parts[0].Id, new GridPoint(20, 0));
            var d1 = sch.AddPart(lib, parts[1].Id, new GridPoint(40, 0));
            var q1 = sch.AddPart(lib, parts[2].Id, new GridPoint(60, 0));
            var r2 = sch.AddPart(lib, parts[0].Id, new GridPoint(80, 0));
            r2.Parameters["resistance"] = "10k";
            void Label(string net, SchematicPart part, string pin) => sch.Labels.Add(new NetLabel { Name = net, Position = Tip(lib, part, pin) });
            Label("VCC", v1, "+");
            Label("GND", v1, "-");
            Label("VCC", r1, "A");
            Label("N1", r1, "B");
            Label("N1", d1, "A");
            Label("N2", d1, "K");
            Label("N2", q1, "C");
            Label("NB", q1, "B");
            Label("GND", q1, "E");
            Label("VCC", r2, "A");
            Label("NB", r2, "B");

            Assert.Equal(new[] { "R1", "D1", "Q1", "R2" }, new[] { r1, d1, q1, r2 }.Select(p => p.RefDes));
            var compiled = CircuitCompiler.Compile(sch, lib);
            Assert.DoesNotContain(compiled.Warnings, w => w.Contains("modellenemedi"));
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            sim.Step(1e-3);

            var led = compiled.Device<Led>("D1")!;
            Assert.InRange(led.Current, 0.006, 0.0115);   // (5 V - Vf - Vce(sat)) / 330 Ω ≈ 9 mA
            Assert.InRange(compiled.PinVoltage(sim, "Q1", "C"), 0.0, 0.5);   // saturated
            Assert.InRange(compiled.PinVoltage(sim, "Q1", "B"), 0.55, 0.9);
            Assert.InRange(compiled.PinVoltage(sim, "R1", "A"), 4.9, 5.01);

            // The KiCad pin numbers reach the PCB side through the pinmap.
            var qDef = lib.Get("npn");
            var qParams = sch.ResolveParameters(lib, q1);
            Assert.Equal(new[] { "1", "2", "3" }, new[] { "B", "C", "E" }.Select(n => qDef.ResolvePad(qDef.FindPin(n)!, qParams)));
            Assert.Equal("R_0805_2012Metric", sch.ResolveParameters(lib, r1)["footprint"]);
        }
    }
}
