/*
 * RoboTwin ESP32 simulation - the core's HTTP server (WebServer.h), the API sketches use: handlers registered with on()
 * for a path (and a method) and onNotFound(), handleClient() serving one request at a time over the world's TCP
 * (WiFiServer / WiFiClient: another board, or the world's network), the request's method, URI, query and form arguments
 * and headers, and a response with its status, headers and content: sized, or chunked with CONTENT_LENGTH_UNKNOWN and
 * sendContent(). Basic authentication; CORS. Files (streamFile, uploads) are not there: the fast mode has no file
 * system behind a server. See WebServer.cpp.
 */
#pragma once
#include <functional>
#include <vector>
#include "Arduino.h"
#include "WiFi.h"

typedef enum {
    HTTP_DELETE = 0,
    HTTP_GET = 1,
    HTTP_HEAD = 2,
    HTTP_POST = 3,
    HTTP_PUT = 4,
    HTTP_CONNECT = 5,
    HTTP_OPTIONS = 6,
    HTTP_TRACE = 7,
    HTTP_PATCH = 28,
    HTTP_ANY = 255,
} HTTPMethod;

enum HTTPAuthMethod { BASIC_AUTH, DIGEST_AUTH };

#define CONTENT_LENGTH_UNKNOWN ((size_t)-1)
#define CONTENT_LENGTH_NOT_SET ((size_t)-2)
#define HTTP_MAX_DATA_WAIT 5000   /* ms to wait for the request's data */
#define WEBSERVER_MAX_ARGS 32
#define WEBSERVER_MAX_HEADERS 24

class WebServer {
public:
    typedef std::function<void(void)> THandlerFunction;

    WebServer(int port = 80);
    WebServer(IPAddress addr, int port = 80);
    ~WebServer();

    void begin();
    void begin(uint16_t port);
    void handleClient();
    void close();
    void stop() { close(); }

    void on(const String& uri, THandlerFunction handler) { on(uri, HTTP_ANY, handler); }
    void on(const String& uri, HTTPMethod method, THandlerFunction fn);
    void on(const String& uri, HTTPMethod method, THandlerFunction fn, THandlerFunction ufn) { (void)ufn; on(uri, method, fn); }
    void onNotFound(THandlerFunction fn) { _notFound = fn; }
    void onFileUpload(THandlerFunction fn) { (void)fn; }

    String uri() const { return _uri; }
    HTTPMethod method() const { return _method; }
    WiFiClient& client() { return _client; }

    String arg(const String& name) const;
    String arg(int i) const;
    String argName(int i) const;
    int args() const { return _argCount; }
    bool hasArg(const String& name) const;

    void collectHeaders(const char* headerKeys[], const size_t headerKeysCount);
    String header(const String& name) const;
    String header(int i) const;
    String headerName(int i) const;
    int headers() const { return _headerCount; }
    bool hasHeader(const String& name) const;
    String hostHeader() const { return header("Host"); }

    bool authenticate(const char* username, const char* password);
    void requestAuthentication(HTTPAuthMethod mode = BASIC_AUTH, const char* realm = NULL, const String& authFailMsg = String(""));

    void send(int code, const char* content_type = NULL, const String& content = String(""));
    void send(int code, char* content_type, const String& content) { send(code, (const char*)content_type, content); }
    void send(int code, const String& content_type, const String& content) { send(code, content_type.c_str(), content); }
    void send(int code, const char* content_type, const char* content) { send(code, content_type, String(content)); }
    void send_P(int code, PGM_P content_type, PGM_P content) { send(code, content_type, String(content)); }
    void send_P(int code, PGM_P content_type, PGM_P content, size_t contentLength) { send(code, content_type, String(content, (unsigned int)contentLength)); }
    void setContentLength(const size_t contentLength) { _contentLength = contentLength; }
    void sendHeader(const String& name, const String& value, bool first = false);
    void sendContent(const String& content) { sendContent(content.c_str(), content.length()); }
    void sendContent(const char* content, size_t size);
    void sendContent_P(PGM_P content) { sendContent(String(content)); }
    void sendContent_P(PGM_P content, size_t size) { sendContent(content, size); }
    void enableCORS(bool value = true) { _cors = value; }
    void enableCrossOrigin(bool value = true) { enableCORS(value); }
    void enableDelay(bool value) { (void)value; }

    static String urlDecode(const String& text);
    static String responseCodeToString(int code);

private:
    struct Route {
        String uri;
        HTTPMethod method;
        THandlerFunction fn;
    };

    bool readRequest();
    void parseArguments(const String& data);
    void finishResponse();

    WiFiServer _server;
    WiFiClient _client;
    std::vector<Route> _routes;
    THandlerFunction _notFound;
    String _uri;
    HTTPMethod _method = HTTP_ANY;
    String _argNames[WEBSERVER_MAX_ARGS], _argValues[WEBSERVER_MAX_ARGS];
    int _argCount = 0;
    String _headerNames[WEBSERVER_MAX_HEADERS], _headerValues[WEBSERVER_MAX_HEADERS];
    int _headerCount = 0;
    String _extraHeaders;
    size_t _contentLength = CONTENT_LENGTH_NOT_SET;
    bool _chunked = false;
    bool _responded = false;
    bool _cors = false;
};
