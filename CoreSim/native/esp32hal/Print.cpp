/* Arduino Print implementation. */
#include "Arduino.h"
#include <time.h>

size_t Print::write(const uint8_t* buffer, size_t size) {
    size_t n = 0;
    while (size--) {
        if (write(*buffer++)) n++;
        else break;
    }

    return n;
}

size_t Print::vprintf(const char* format, va_list arg) {
    char loc[128];
    va_list copy;
    va_copy(copy, arg);
    int len = vsnprintf(loc, sizeof loc, format, copy);
    va_end(copy);
    if (len < 0) return 0;
    if ((size_t)len < sizeof loc) return write(reinterpret_cast<const uint8_t*>(loc), (size_t)len);
    char* temp = new char[(size_t)len + 1];
    vsnprintf(temp, (size_t)len + 1, format, arg);
    size_t n = write(reinterpret_cast<const uint8_t*>(temp), (size_t)len);
    delete[] temp;
    return n;
}

/* Declared as printf(); the Arduino.h printf macro renames it consistently. */
size_t Print::printf(const char* format, ...) {
    va_list arg;
    va_start(arg, format);
    size_t n = vprintf(format, arg);
    va_end(arg);
    return n;
}

size_t Print::print(const String& s) { return write(reinterpret_cast<const uint8_t*>(s.c_str()), s.length()); }
size_t Print::print(const char str[]) { return write(str); }
size_t Print::print(char c) { return write((uint8_t)c); }
size_t Print::print(unsigned char b, int base) { return print((unsigned long long)b, base); }
size_t Print::print(int n, int base) { return base == 0 ? write((uint8_t)n) : printSigned(n, base); }
size_t Print::print(unsigned int n, int base) { return print((unsigned long long)n, base); }
size_t Print::print(long n, int base) { return base == 0 ? write((uint8_t)n) : printSigned(n, base); }
size_t Print::print(unsigned long n, int base) { return print((unsigned long long)n, base); }
size_t Print::print(long long n, int base) {
    if (base == 0) return write((uint8_t)n);
    if (base != 10) return printNumber((unsigned long long)n, (uint8_t)base);
    return printSigned(n, base);
}

size_t Print::print(unsigned long long n, int base) {
    if (base == 0) return write((uint8_t)n);
    return printNumber(n, (uint8_t)base);
}

size_t Print::print(double n, int digits) { return printFloat(n, (uint8_t)(digits < 0 ? 0 : digits)); }
size_t Print::print(const Printable& x) { return x.printTo(*this); }

size_t Print::print(struct tm* timeinfo, const char* format) {
    const char* f = format ? format : "%c";
    char buf[64];
    size_t written = strftime(buf, sizeof buf, f, timeinfo);
    return written ? print(buf) : 0;
}

size_t Print::println(void) { return print("\r\n"); }
size_t Print::println(const String& s) { size_t n = print(s); return n + println(); }
size_t Print::println(const char c[]) { size_t n = print(c); return n + println(); }
size_t Print::println(char c) { size_t n = print(c); return n + println(); }
size_t Print::println(unsigned char b, int base) { size_t n = print(b, base); return n + println(); }
size_t Print::println(int num, int base) { size_t n = print(num, base); return n + println(); }
size_t Print::println(unsigned int num, int base) { size_t n = print(num, base); return n + println(); }
size_t Print::println(long num, int base) { size_t n = print(num, base); return n + println(); }
size_t Print::println(unsigned long num, int base) { size_t n = print(num, base); return n + println(); }
size_t Print::println(long long num, int base) { size_t n = print(num, base); return n + println(); }
size_t Print::println(unsigned long long num, int base) { size_t n = print(num, base); return n + println(); }
size_t Print::println(double num, int digits) { size_t n = print(num, digits); return n + println(); }
size_t Print::println(const Printable& x) { size_t n = print(x); return n + println(); }
size_t Print::println(struct tm* timeinfo, const char* format) { size_t n = print(timeinfo, format); return n + println(); }

size_t Print::printSigned(long long n, int base) {
    if (base == 10 && n < 0) {
        size_t t = print('-');
        return t + printNumber((unsigned long long)(-(n + 1)) + 1ull, 10);
    }

    // Like the core, negative numbers in other bases print their two's complement.
    if (n < 0 && n >= INT32_MIN) return printNumber((unsigned long long)(uint32_t)(int32_t)n, (uint8_t)base);
    return printNumber((unsigned long long)n, (uint8_t)base);
}

size_t Print::printNumber(unsigned long long n, uint8_t base) {
    char buf[8 * sizeof(n) + 1];
    char* str = &buf[sizeof(buf) - 1];
    *str = '\0';
    if (base < 2) base = 10;
    do {
        unsigned long long m = n;
        n /= base;
        char c = (char)(m - base * n);
        *--str = c < 10 ? c + '0' : c + 'A' - 10;
    } while (n);
    return write(str);
}

size_t Print::printFloat(double number, uint8_t digits) {
    size_t n = 0;
    if (isnan(number)) return print("nan");
    if (isinf(number)) return print("inf");
    if (number > 4294967040.0) return print("ovf");
    if (number < -4294967040.0) return print("ovf");

    if (number < 0.0) {
        n += print('-');
        number = -number;
    }

    double rounding = 0.5;
    for (uint8_t i = 0; i < digits; ++i) rounding /= 10.0;
    number += rounding;

    unsigned long int_part = (unsigned long)number;
    double remainder = number - (double)int_part;
    n += print(int_part);
    if (digits > 0) n += print('.');
    while (digits-- > 0) {
        remainder *= 10.0;
        unsigned int toPrint = (unsigned int)remainder;
        n += print(toPrint);
        remainder -= toPrint;
    }

    return n;
}
