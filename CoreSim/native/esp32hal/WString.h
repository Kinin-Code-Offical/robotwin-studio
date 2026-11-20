/* Arduino String class (WString), compatible with the Arduino-ESP32 core API. */
#ifndef RW_WSTRING_H
#define RW_WSTRING_H

#ifdef __cplusplus
#include <stdint.h>
#include <stddef.h>
#include <string.h>

class __FlashStringHelper;
#define FPSTR(pstr_pointer) (reinterpret_cast<const __FlashStringHelper*>(pstr_pointer))
#define F(string_literal) (FPSTR(string_literal))

class String {
public:
    String(const char* cstr = "");
    String(const char* cstr, unsigned int length);
    String(const uint8_t* cstr, unsigned int length) : String(reinterpret_cast<const char*>(cstr), length) {}
    String(const String& str);
    String(const __FlashStringHelper* str) : String(reinterpret_cast<const char*>(str)) {}
    String(String&& rval) noexcept;
    explicit String(char c);
    explicit String(unsigned char value, unsigned char base = 10);
    explicit String(int value, unsigned char base = 10);
    explicit String(unsigned int value, unsigned char base = 10);
    explicit String(long value, unsigned char base = 10);
    explicit String(unsigned long value, unsigned char base = 10);
    explicit String(long long value, unsigned char base = 10);
    explicit String(unsigned long long value, unsigned char base = 10);
    explicit String(float value, unsigned int decimalPlaces = 2);
    explicit String(double value, unsigned int decimalPlaces = 2);
    ~String();

    bool reserve(unsigned int size);
    inline unsigned int length() const { return _len; }
    inline bool isEmpty() const { return _len == 0; }
    void clear() { _len = 0; if (_buf) _buf[0] = 0; }

    String& operator=(const String& rhs);
    String& operator=(const char* cstr);
    String& operator=(const __FlashStringHelper* str) { return *this = reinterpret_cast<const char*>(str); }
    String& operator=(String&& rval) noexcept;

    bool concat(const String& str);
    bool concat(const char* cstr);
    bool concat(const char* cstr, unsigned int length);
    bool concat(const uint8_t* cstr, unsigned int length) { return concat(reinterpret_cast<const char*>(cstr), length); }
    bool concat(char c);
    bool concat(unsigned char num);
    bool concat(int num);
    bool concat(unsigned int num);
    bool concat(long num);
    bool concat(unsigned long num);
    bool concat(long long num);
    bool concat(unsigned long long num);
    bool concat(float num);
    bool concat(double num);
    bool concat(const __FlashStringHelper* str) { return concat(reinterpret_cast<const char*>(str)); }

    String& operator+=(const String& rhs) { concat(rhs); return *this; }
    String& operator+=(const char* cstr) { concat(cstr); return *this; }
    String& operator+=(char c) { concat(c); return *this; }
    String& operator+=(unsigned char num) { concat(num); return *this; }
    String& operator+=(int num) { concat(num); return *this; }
    String& operator+=(unsigned int num) { concat(num); return *this; }
    String& operator+=(long num) { concat(num); return *this; }
    String& operator+=(unsigned long num) { concat(num); return *this; }
    String& operator+=(long long num) { concat(num); return *this; }
    String& operator+=(unsigned long long num) { concat(num); return *this; }
    String& operator+=(float num) { concat(num); return *this; }
    String& operator+=(double num) { concat(num); return *this; }
    String& operator+=(const __FlashStringHelper* str) { concat(str); return *this; }

    explicit operator bool() const { return _buf != nullptr; }

    int compareTo(const String& s) const;
    int compareTo(const char* cstr) const;
    bool equals(const String& s) const;
    bool equals(const char* cstr) const;
    bool operator==(const String& rhs) const { return equals(rhs); }
    bool operator==(const char* cstr) const { return equals(cstr); }
    bool operator!=(const String& rhs) const { return !equals(rhs); }
    bool operator!=(const char* cstr) const { return !equals(cstr); }
    bool operator<(const String& rhs) const { return compareTo(rhs) < 0; }
    bool operator>(const String& rhs) const { return compareTo(rhs) > 0; }
    bool operator<=(const String& rhs) const { return compareTo(rhs) <= 0; }
    bool operator>=(const String& rhs) const { return compareTo(rhs) >= 0; }
    bool equalsIgnoreCase(const String& s) const;
    bool equalsConstantTime(const String& s) const { return equals(s); }
    bool startsWith(const String& prefix) const;
    bool startsWith(const String& prefix, unsigned int offset) const;
    bool endsWith(const String& suffix) const;

    char charAt(unsigned int index) const;
    void setCharAt(unsigned int index, char c);
    char operator[](unsigned int index) const;
    char& operator[](unsigned int index);
    void getBytes(unsigned char* buf, unsigned int bufsize, unsigned int index = 0) const;
    void toCharArray(char* buf, unsigned int bufsize, unsigned int index = 0) const { getBytes(reinterpret_cast<unsigned char*>(buf), bufsize, index); }
    const char* c_str() const { return _buf ? _buf : ""; }
    char* begin() { return _buf; }
    char* end() { return _buf ? _buf + _len : nullptr; }
    const char* begin() const { return c_str(); }
    const char* end() const { return c_str() + _len; }

    int indexOf(char ch) const { return indexOf(ch, 0); }
    int indexOf(char ch, unsigned int fromIndex) const;
    int indexOf(const String& str) const { return indexOf(str, 0); }
    int indexOf(const String& str, unsigned int fromIndex) const;
    int indexOf(const char* str) const { return indexOf(String(str), 0); }
    int indexOf(const char* str, unsigned int fromIndex) const { return indexOf(String(str), fromIndex); }
    int lastIndexOf(char ch) const;
    int lastIndexOf(char ch, unsigned int fromIndex) const;
    int lastIndexOf(const String& str) const;
    int lastIndexOf(const String& str, unsigned int fromIndex) const;
    String substring(unsigned int beginIndex) const { return substring(beginIndex, _len); }
    String substring(unsigned int beginIndex, unsigned int endIndex) const;

    void replace(char find, char replace);
    void replace(const String& find, const String& replace);
    void replace(const char* find, const String& replace) { this->replace(String(find), replace); }
    void replace(const char* find, const char* replace) { this->replace(String(find), String(replace)); }
    void remove(unsigned int index);
    void remove(unsigned int index, unsigned int count);
    void toLowerCase(void);
    void toUpperCase(void);
    void trim(void);

    long toInt(void) const;
    float toFloat(void) const;
    double toDouble(void) const;

private:
    char* _buf = nullptr;
    unsigned int _len = 0;
    unsigned int _cap = 0;
    bool grow(unsigned int size);
    String& copy(const char* cstr, unsigned int length);
    void move(String& rhs) noexcept;
    void release();
};

String operator+(const String& lhs, const String& rhs);
String operator+(const String& lhs, const char* rhs);
String operator+(const char* lhs, const String& rhs);
String operator+(const String& lhs, char rhs);
String operator+(char lhs, const String& rhs);
String operator+(const String& lhs, unsigned char rhs);
String operator+(const String& lhs, int rhs);
String operator+(const String& lhs, unsigned int rhs);
String operator+(const String& lhs, long rhs);
String operator+(const String& lhs, unsigned long rhs);
String operator+(const String& lhs, long long rhs);
String operator+(const String& lhs, unsigned long long rhs);
String operator+(const String& lhs, float rhs);
String operator+(const String& lhs, double rhs);
String operator+(const String& lhs, const __FlashStringHelper* rhs);
inline bool operator==(const char* lhs, const String& rhs) { return rhs.equals(lhs); }
inline bool operator!=(const char* lhs, const String& rhs) { return !rhs.equals(lhs); }

#endif /* __cplusplus */
#endif
