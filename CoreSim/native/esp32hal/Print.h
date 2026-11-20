/* Arduino Print: print/println for every common type and base, plus printf. */
#ifndef RW_PRINT_H
#define RW_PRINT_H
#ifdef __cplusplus
#include <stdint.h>
#include <stddef.h>
#include <stdarg.h>
#include <string.h>
#include "WString.h"
#include "Printable.h"

#ifndef DEC
#define DEC 10
#define HEX 16
#define OCT 8
#define BIN 2
#endif

struct tm;

class Print {
public:
    Print() : write_error(0) {}
    virtual ~Print() {}

    int getWriteError() { return write_error; }
    void clearWriteError() { setWriteError(0); }

    virtual size_t write(uint8_t) = 0;
    size_t write(const char* str) { return str ? write(reinterpret_cast<const uint8_t*>(str), strlen(str)) : 0; }
    virtual size_t write(const uint8_t* buffer, size_t size);
    size_t write(const char* buffer, size_t size) { return write(reinterpret_cast<const uint8_t*>(buffer), size); }
    virtual int availableForWrite() { return 0; }
    virtual void flush() {}

    size_t printf(const char* format, ...);
    size_t vprintf(const char* format, va_list arg);

    size_t print(const __FlashStringHelper* ifsh) { return print(reinterpret_cast<const char*>(ifsh)); }
    size_t print(const String& s);
    size_t print(const char str[]);
    size_t print(char c);
    size_t print(unsigned char n, int base = DEC);
    size_t print(int n, int base = DEC);
    size_t print(unsigned int n, int base = DEC);
    size_t print(long n, int base = DEC);
    size_t print(unsigned long n, int base = DEC);
    size_t print(long long n, int base = DEC);
    size_t print(unsigned long long n, int base = DEC);
    size_t print(double n, int digits = 2);
    size_t print(const Printable& x);
    size_t print(struct tm* timeinfo, const char* format = nullptr);

    size_t println(const __FlashStringHelper* ifsh) { return println(reinterpret_cast<const char*>(ifsh)); }
    size_t println(const String& s);
    size_t println(const char str[]);
    size_t println(char c);
    size_t println(unsigned char n, int base = DEC);
    size_t println(int n, int base = DEC);
    size_t println(unsigned int n, int base = DEC);
    size_t println(long n, int base = DEC);
    size_t println(unsigned long n, int base = DEC);
    size_t println(long long n, int base = DEC);
    size_t println(unsigned long long n, int base = DEC);
    size_t println(double n, int digits = 2);
    size_t println(const Printable& x);
    size_t println(struct tm* timeinfo, const char* format = nullptr);
    size_t println(void);

protected:
    void setWriteError(int err = 1) { write_error = err; }

private:
    int write_error;
    size_t printNumber(unsigned long long n, uint8_t base);
    size_t printSigned(long long n, int base);
    size_t printFloat(double number, uint8_t digits);
};

#endif
#endif
