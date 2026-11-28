/* Arduino's server interface (Server.h), for the libraries that include it; the ESP32's WiFiServer is a class of its own
 * (as on the 3.x core), not one of these. */
#ifndef RW_SERVER_H
#define RW_SERVER_H
#include "Print.h"

class Server : public Print {
public:
    virtual void begin() = 0;
};
#endif
