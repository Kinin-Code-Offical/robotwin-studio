/* Arduino String implementation (private heap, always NUL terminated). */
#include "Arduino.h"
#include "rw_internal.h"

namespace {

void utoa_base(unsigned long long value, char* out, unsigned base) {
    char tmp[72];
    int n = 0;
    if (base < 2 || base > 36) base = 10;
    do {
        unsigned d = (unsigned)(value % base);
        tmp[n++] = (char)(d < 10 ? '0' + d : 'a' + d - 10);
        value /= base;
    } while (value && n < 70);
    int i = 0;
    while (n > 0) out[i++] = tmp[--n];
    out[i] = 0;
}

void itoa_base(long long value, char* out, unsigned base) {
    if (value < 0 && base == 10) {
        out[0] = '-';
        utoa_base((unsigned long long)(-(value + 1)) + 1ull, out + 1, base);
    } else {
        utoa_base((unsigned long long)value, out, base);
    }
}

void dtostr(double value, unsigned decimals, char* out, size_t cap) {
    if (decimals > 30) decimals = 30;
    if (value != value) { snprintf(out, cap, "nan"); return; }
    if (value > 4294967040.0 || value < -4294967040.0) { snprintf(out, cap, "ovf"); return; }
    snprintf(out, cap, "%.*f", (int)decimals, value);
}

}  // namespace

String::String(const char* cstr) {
    if (cstr) copy(cstr, (unsigned)strlen(cstr));
    else copy("", 0);
}

String::String(const char* cstr, unsigned int length) {
    if (cstr) copy(cstr, length);
    else copy("", 0);
}

String::String(const String& str) { copy(str.c_str(), str._len); }

String::String(String&& rval) noexcept { move(rval); }

String::String(char c) {
    char buf[2] = {c, 0};
    copy(buf, 1);
}

String::String(unsigned char value, unsigned char base) {
    char buf[72];
    utoa_base(value, buf, base);
    copy(buf, (unsigned)strlen(buf));
}

String::String(int value, unsigned char base) {
    char buf[72];
    if (base == 10) itoa_base(value, buf, base);
    else utoa_base((unsigned int)value, buf, base);
    copy(buf, (unsigned)strlen(buf));
}

String::String(unsigned int value, unsigned char base) {
    char buf[72];
    utoa_base(value, buf, base);
    copy(buf, (unsigned)strlen(buf));
}

String::String(long value, unsigned char base) {
    char buf[72];
    if (base == 10) itoa_base(value, buf, base);
    else utoa_base((unsigned long)value, buf, base);
    copy(buf, (unsigned)strlen(buf));
}

String::String(unsigned long value, unsigned char base) {
    char buf[72];
    utoa_base(value, buf, base);
    copy(buf, (unsigned)strlen(buf));
}

String::String(long long value, unsigned char base) {
    char buf[72];
    if (base == 10) itoa_base(value, buf, base);
    else utoa_base((unsigned long long)value, buf, base);
    copy(buf, (unsigned)strlen(buf));
}

String::String(unsigned long long value, unsigned char base) {
    char buf[72];
    utoa_base(value, buf, base);
    copy(buf, (unsigned)strlen(buf));
}

String::String(float value, unsigned int decimalPlaces) {
    char buf[64];
    dtostr(value, decimalPlaces, buf, sizeof buf);
    copy(buf, (unsigned)strlen(buf));
}

String::String(double value, unsigned int decimalPlaces) {
    char buf[64];
    dtostr(value, decimalPlaces, buf, sizeof buf);
    copy(buf, (unsigned)strlen(buf));
}

String::~String() { release(); }

void String::release() {
    if (_buf) rw::heap_free(_buf);
    _buf = nullptr;
    _len = 0;
    _cap = 0;
}

bool String::grow(unsigned int size) {
    if (_buf && _cap >= size) return true;
    unsigned int cap = _cap ? _cap : 16;
    while (cap < size) cap *= 2;
    char* p = static_cast<char*>(rw::heap_realloc(_buf, cap + 1));
    if (!p) return false;
    if (!_buf) p[0] = 0;
    _buf = p;
    _cap = cap;
    return true;
}

bool String::reserve(unsigned int size) {
    if (!grow(size)) return false;
    if (_len == 0) _buf[0] = 0;
    return true;
}

String& String::copy(const char* cstr, unsigned int length) {
    if (!grow(length)) {
        release();
        return *this;
    }

    memmove(_buf, cstr, length);
    _buf[length] = 0;
    _len = length;
    return *this;
}

void String::move(String& rhs) noexcept {
    if (this == &rhs) return;
    if (_buf) rw::heap_free(_buf);
    _buf = rhs._buf;
    _len = rhs._len;
    _cap = rhs._cap;
    rhs._buf = nullptr;
    rhs._len = 0;
    rhs._cap = 0;
}

String& String::operator=(const String& rhs) {
    if (this == &rhs) return *this;
    return copy(rhs.c_str(), rhs._len);
}

String& String::operator=(const char* cstr) {
    if (!cstr) return copy("", 0);
    if (_buf && cstr >= _buf && cstr <= _buf + _len) {
        String tmp(cstr);
        move(tmp);
        return *this;
    }

    return copy(cstr, (unsigned)strlen(cstr));
}

String& String::operator=(String&& rval) noexcept {
    move(rval);
    return *this;
}

bool String::concat(const char* cstr, unsigned int length) {
    if (!cstr) return false;
    if (length == 0) return true;
    unsigned int newlen = _len + length;
    if (_buf && cstr >= _buf && cstr < _buf + _cap) {
        // Appending part of ourselves: copy first because grow() may move the buffer.
        String tmp(cstr, length);
        return concat(tmp.c_str(), length);
    }

    if (!grow(newlen)) return false;
    memcpy(_buf + _len, cstr, length);
    _len = newlen;
    _buf[_len] = 0;
    return true;
}

bool String::concat(const String& s) { return concat(s.c_str(), s._len); }
bool String::concat(const char* cstr) { return cstr ? concat(cstr, (unsigned)strlen(cstr)) : false; }

bool String::concat(char c) {
    char buf[2] = {c, 0};
    return concat(buf, 1);
}

bool String::concat(unsigned char num) { return concat(String(num)); }
bool String::concat(int num) { return concat(String(num)); }
bool String::concat(unsigned int num) { return concat(String(num)); }
bool String::concat(long num) { return concat(String(num)); }
bool String::concat(unsigned long num) { return concat(String(num)); }
bool String::concat(long long num) { return concat(String(num)); }
bool String::concat(unsigned long long num) { return concat(String(num)); }
bool String::concat(float num) { return concat(String(num)); }
bool String::concat(double num) { return concat(String(num)); }

String operator+(const String& lhs, const String& rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, const char* rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const char* lhs, const String& rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, char rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(char lhs, const String& rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, unsigned char rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, int rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, unsigned int rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, long rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, unsigned long rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, long long rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, unsigned long long rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, float rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, double rhs) { String s(lhs); s.concat(rhs); return s; }
String operator+(const String& lhs, const __FlashStringHelper* rhs) { String s(lhs); s.concat(rhs); return s; }

int String::compareTo(const String& s) const { return strcmp(c_str(), s.c_str()); }
int String::compareTo(const char* cstr) const { return strcmp(c_str(), cstr ? cstr : ""); }

bool String::equals(const String& s) const { return _len == s._len && compareTo(s) == 0; }
bool String::equals(const char* cstr) const { return strcmp(c_str(), cstr ? cstr : "") == 0; }

bool String::equalsIgnoreCase(const String& s) const {
    if (_len != s._len) return false;
    const char* a = c_str();
    const char* b = s.c_str();
    for (unsigned i = 0; i < _len; i++) {
        if (tolower((unsigned char)a[i]) != tolower((unsigned char)b[i])) return false;
    }

    return true;
}

bool String::startsWith(const String& prefix) const {
    if (_len < prefix._len) return false;
    return startsWith(prefix, 0);
}

bool String::startsWith(const String& prefix, unsigned int offset) const {
    if (offset > _len || _len - offset < prefix._len) return false;
    return strncmp(c_str() + offset, prefix.c_str(), prefix._len) == 0;
}

bool String::endsWith(const String& suffix) const {
    if (_len < suffix._len) return false;
    return strcmp(c_str() + _len - suffix._len, suffix.c_str()) == 0;
}

char String::charAt(unsigned int index) const { return operator[](index); }

void String::setCharAt(unsigned int index, char c) {
    if (index < _len) _buf[index] = c;
}

char String::operator[](unsigned int index) const {
    if (index >= _len || !_buf) return 0;
    return _buf[index];
}

char& String::operator[](unsigned int index) {
    static char dummy;
    if (index >= _len || !_buf) {
        dummy = 0;
        return dummy;
    }

    return _buf[index];
}

void String::getBytes(unsigned char* buf, unsigned int bufsize, unsigned int index) const {
    if (!bufsize || !buf) return;
    if (index >= _len) {
        buf[0] = 0;
        return;
    }

    unsigned int n = bufsize - 1;
    if (n > _len - index) n = _len - index;
    memcpy(buf, c_str() + index, n);
    buf[n] = 0;
}

int String::indexOf(char ch, unsigned int fromIndex) const {
    if (fromIndex >= _len) return -1;
    const char* p = strchr(c_str() + fromIndex, ch);
    return p ? (int)(p - c_str()) : -1;
}

int String::indexOf(const String& s, unsigned int fromIndex) const {
    if (fromIndex >= _len) return -1;
    const char* p = strstr(c_str() + fromIndex, s.c_str());
    return p ? (int)(p - c_str()) : -1;
}

int String::lastIndexOf(char ch) const { return _len ? lastIndexOf(ch, _len - 1) : -1; }

int String::lastIndexOf(char ch, unsigned int fromIndex) const {
    if (fromIndex >= _len) return -1;
    for (int i = (int)fromIndex; i >= 0; i--) {
        if (_buf[i] == ch) return i;
    }

    return -1;
}

int String::lastIndexOf(const String& s) const { return _len >= s._len ? lastIndexOf(s, _len - s._len) : -1; }

int String::lastIndexOf(const String& s, unsigned int fromIndex) const {
    if (s._len == 0 || _len == 0 || s._len > _len) return -1;
    if (fromIndex >= _len) fromIndex = _len - 1;
    int found = -1;
    for (const char* p = c_str(); p <= c_str() + fromIndex; p++) {
        p = strstr(p, s.c_str());
        if (!p) break;
        if ((unsigned)(p - c_str()) <= fromIndex) found = (int)(p - c_str());
    }

    return found;
}

String String::substring(unsigned int left, unsigned int right) const {
    if (left > right) {
        unsigned int t = right;
        right = left;
        left = t;
    }

    if (left >= _len) return String();
    if (right > _len) right = _len;
    return String(c_str() + left, right - left);
}

void String::replace(char find, char replace) {
    for (unsigned i = 0; i < _len; i++) {
        if (_buf[i] == find) _buf[i] = replace;
    }
}

void String::replace(const String& find, const String& replace) {
    if (_len == 0 || find._len == 0) return;
    String out;
    out.reserve(_len);
    const char* p = c_str();
    for (;;) {
        const char* hit = strstr(p, find.c_str());
        if (!hit) break;
        out.concat(p, (unsigned)(hit - p));
        out.concat(replace);
        p = hit + find._len;
    }

    out.concat(p);
    move(out);
}

void String::remove(unsigned int index) { remove(index, (unsigned int)-1); }

void String::remove(unsigned int index, unsigned int count) {
    if (index >= _len || count == 0) return;
    if (count > _len - index) count = _len - index;
    memmove(_buf + index, _buf + index + count, _len - index - count + 1);
    _len -= count;
}

void String::toLowerCase(void) {
    for (unsigned i = 0; i < _len; i++) _buf[i] = (char)tolower((unsigned char)_buf[i]);
}

void String::toUpperCase(void) {
    for (unsigned i = 0; i < _len; i++) _buf[i] = (char)toupper((unsigned char)_buf[i]);
}

void String::trim(void) {
    if (!_buf || _len == 0) return;
    char* begin = _buf;
    while (isspace((unsigned char)*begin)) begin++;
    char* end = _buf + _len - 1;
    while (end >= begin && isspace((unsigned char)*end)) end--;
    _len = (unsigned)(end + 1 - begin);
    if (begin > _buf) memmove(_buf, begin, _len);
    _buf[_len] = 0;
}

long String::toInt(void) const { return _buf ? atol(_buf) : 0; }
float String::toFloat(void) const { return _buf ? (float)atof(_buf) : 0.0f; }
double String::toDouble(void) const { return _buf ? atof(_buf) : 0.0; }
