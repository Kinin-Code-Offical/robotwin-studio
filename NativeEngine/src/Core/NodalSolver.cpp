#include "Core/NodalSolver.hpp"
#include <cmath>

namespace solver
{
    static void Solve2x2(double a, double b, double c, double d, double i1, double i2, double& v1, double& v2)
    {
        double det = a * d - b * c;
        if (std::abs(det) < 1e-12)
        {
            v1 = 0.0;
            v2 = 0.0;
            return;
        }
        v1 = (i1 * d - b * i2) / det;
        v2 = (a * i2 - i1 * c) / det;
    }

    BlinkResult SolveBlink(double pin_voltage, const BlinkParams& params)
    {
        BlinkResult result;
        result.v_gnd = params.logic_low;
        result.v_vcc = params.logic_high;

        double g_r = 1.0 / params.resistor_ohms;
        double g_src = 1.0 / params.driver_ohms;
        double g_d = 1.0 / params.diode_off_ohms;
        double vf = 0.0;

        double v1 = pin_voltage;
        double v2 = 0.0;

        for (int iter = 0; iter < 3; ++iter)
        {
            double a = g_r + g_src;
            double b = -g_r;
            double c = -g_r;
            double d = g_r + g_d;
            double i1 = g_src * pin_voltage;
            double i2 = g_d * vf;
            Solve2x2(a, b, c, d, i1, i2, v1, v2);
            if (v2 >= params.led_vf)
            {
                g_d = 1.0 / params.led_rd;
                vf = params.led_vf;
            }
            else
            {
                g_d = 1.0 / params.diode_off_ohms;
                vf = 0.0;
            }
        }

        double i_res = (v1 - v2) / params.resistor_ohms;
        double i_led = g_d * (v2 - vf);
        double p_res = i_res * i_res * params.resistor_ohms;

        result.v_d13 = v1;
        result.v_led = v2;
        result.i_res = i_res;
        result.i_led = i_led;
        result.p_res = p_res;
        result.led_over = i_led > params.led_i_max;
        result.resistor_over = p_res > params.resistor_watts;
        return result;
    }
}
