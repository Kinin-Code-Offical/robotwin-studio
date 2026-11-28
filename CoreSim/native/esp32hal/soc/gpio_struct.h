/* The ESP32's GPIO registers (soc/gpio_struct.h, the `GPIO` register file) on the simulation's pins.
 * Libraries that switch pins faster than digitalWrite (OneWire's bit-banged bus, software SPI, ...) write the output and
 * enable registers and their set / clear twins and read the input register. Each access goes to the pins it names: a
 * write to every pin whose bit it carries (as pinMode / digitalWrite would leave it, without their checks), a read only to
 * the pins a mask or shift then keeps, so `(GPIO.in >> pin) & 1` reads one pin. GPIO32-39 are in the `...1.val` registers. */
#ifndef RW_SOC_GPIO_STRUCT_H
#define RW_SOC_GPIO_STRUCT_H
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif
/* reg: one of rw_gpio::Reg; mask: the bits (pins of the register's bank) wanted */
uint32_t rw_gpio_reg_read(int reg, uint32_t mask);
void rw_gpio_reg_write(int reg, uint32_t value);
#ifdef __cplusplus
}

#include <type_traits>

namespace rw_gpio {
/* The operators take any integer exactly (a template), so `(GPIO.in >> pin) & 1` is theirs, not the built-in one's
   through the conversion to uint32_t (which would make the call ambiguous). */
template <typename T>
using IfInteger = typename std::enable_if<std::is_integral<T>::value, int>::type;

enum Reg { Out, OutSet, OutClear, Out1, Out1Set, Out1Clear, Enable, EnableSet, EnableClear, Enable1, Enable1Set, Enable1Clear, In, In1 };

/* A register read through a right shift: the mask that follows says which pins are read. */
struct Shifted {
    int reg;
    int by;
    template <typename T, IfInteger<T> = 0>
    uint32_t operator&(T mask) const { return by >= 32 ? 0 : rw_gpio_reg_read(reg, (uint32_t)mask << by) >> by; }
    operator uint32_t() const { return by >= 32 ? 0 : rw_gpio_reg_read(reg, 0xFFFFFFFFu << by) >> by; }
};

template <int R>
struct Reg32 {
    operator uint32_t() const { return rw_gpio_reg_read(R, 0xFFFFFFFFu); }
    template <typename T, IfInteger<T> = 0>
    uint32_t operator&(T mask) const { return rw_gpio_reg_read(R, (uint32_t)mask); }
    template <typename T, IfInteger<T> = 0>
    Shifted operator>>(T n) const { return Shifted{R, (int)n}; }
    Reg32& operator=(uint32_t v) {
        rw_gpio_reg_write(R, v);
        return *this;
    }
    Reg32& operator|=(uint32_t v) {
        rw_gpio_reg_write(R, rw_gpio_reg_read(R, 0xFFFFFFFFu) | v);
        return *this;
    }
    Reg32& operator&=(uint32_t v) {
        rw_gpio_reg_write(R, rw_gpio_reg_read(R, 0xFFFFFFFFu) & v);
        return *this;
    }
};

/* The registers of GPIO32-39 are unions on the chip, written and read through .val. */
template <int R>
struct Reg32Val {
    Reg32<R> val;
};
}  // namespace rw_gpio

typedef struct {
    rw_gpio::Reg32<rw_gpio::Out> out;
    rw_gpio::Reg32<rw_gpio::OutSet> out_w1ts;
    rw_gpio::Reg32<rw_gpio::OutClear> out_w1tc;
    rw_gpio::Reg32Val<rw_gpio::Out1> out1;
    rw_gpio::Reg32Val<rw_gpio::Out1Set> out1_w1ts;
    rw_gpio::Reg32Val<rw_gpio::Out1Clear> out1_w1tc;
    rw_gpio::Reg32<rw_gpio::Enable> enable;
    rw_gpio::Reg32<rw_gpio::EnableSet> enable_w1ts;
    rw_gpio::Reg32<rw_gpio::EnableClear> enable_w1tc;
    rw_gpio::Reg32Val<rw_gpio::Enable1> enable1;
    rw_gpio::Reg32Val<rw_gpio::Enable1Set> enable1_w1ts;
    rw_gpio::Reg32Val<rw_gpio::Enable1Clear> enable1_w1tc;
    rw_gpio::Reg32<rw_gpio::In> in;
    rw_gpio::Reg32Val<rw_gpio::In1> in1;
} gpio_dev_t;

/* holds nothing itself: every access goes to the pins */
static gpio_dev_t GPIO;
#endif /* __cplusplus */
#endif
