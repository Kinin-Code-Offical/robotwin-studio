/* Arduino Stream: buffered input with timeouts and parsing helpers. */
#ifndef RW_STREAM_H
#define RW_STREAM_H
#ifdef __cplusplus
#include "Print.h"

enum LookaheadMode { SKIP_ALL, SKIP_NONE, SKIP_WHITESPACE };
#define NO_IGNORE_CHAR '\x01'

class Stream : public Print {
protected:
    unsigned long _timeout;
    unsigned long _startMillis;
    int timedRead();
    int timedPeek();
    int peekNextDigit(LookaheadMode lookahead, bool detectDecimal);
    /* Blocks the calling task until more data may have arrived or the deadline (millis()) passes. */
    virtual void waitForData(unsigned long deadlineMillis);

public:
    virtual int available() = 0;
    virtual int read() = 0;
    virtual int peek() = 0;

    Stream() : _timeout(1000), _startMillis(0) {}
    virtual ~Stream() {}

    void setTimeout(unsigned long timeout) { _timeout = timeout; }
    unsigned long getTimeout(void) { return _timeout; }

    bool find(const char* target);
    bool find(const uint8_t* target) { return find(reinterpret_cast<const char*>(target)); }
    bool find(const char* target, size_t length);
    bool find(const uint8_t* target, size_t length) { return find(reinterpret_cast<const char*>(target), length); }
    bool find(char target) { return find(&target, 1); }
    bool findUntil(const char* target, const char* terminator);
    bool findUntil(const char* target, size_t targetLen, const char* terminate, size_t termLen);

    long parseInt(LookaheadMode lookahead = SKIP_ALL, char ignore = NO_IGNORE_CHAR);
    float parseFloat(LookaheadMode lookahead = SKIP_ALL, char ignore = NO_IGNORE_CHAR);

    virtual size_t readBytes(char* buffer, size_t length);
    virtual size_t readBytes(uint8_t* buffer, size_t length) { return readBytes(reinterpret_cast<char*>(buffer), length); }
    size_t readBytesUntil(char terminator, char* buffer, size_t length);
    size_t readBytesUntil(char terminator, uint8_t* buffer, size_t length) { return readBytesUntil(terminator, reinterpret_cast<char*>(buffer), length); }

    virtual String readString();
    String readStringUntil(char terminator);

protected:
    struct MultiTarget { const char* str; size_t len; size_t index; };
    int findMulti(struct MultiTarget* targets, int tCount);
};

#endif
#endif
