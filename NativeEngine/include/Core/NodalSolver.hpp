#pragma once
#include <cstdint>

namespace solver
{
    struct BlinkParams
    {
        double logic_high = 5.0;
        double logic_low = 0.0;
        double led_vf = 2.0;
        double led_i_max = 0.020;
        double led_rd = 15.0;
        double resistor_ohms = 220.0;
        double resistor_watts = 0.25;
        double driver_ohms = 1.0;
        double diode_off_ohms = 1e9;
    };

    struct BlinkResult
    {
        double v_gnd = 0.0;
        double v_vcc = 5.0;
        double v_d13 = 0.0;
        double v_led = 0.0;
        double i_res = 0.0;
        double i_led = 0.0;
        double p_res = 0.0;
        bool led_over = false;
        bool resistor_over = false;
    };

    BlinkResult SolveBlink(double pin_voltage, const BlinkParams& params);
}
