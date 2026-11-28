/*
 * RoboTwin ESP32 simulation - WebServer (see WebServer.h): one HTTP/1.1 request at a time over the world's TCP. The
 * request is read until its blank line (and its Content-Length of body) within HTTP_MAX_DATA_WAIT of simulated time;
 * GET arguments come from the query, POST / PUT ones also from an application/x-www-form-urlencoded body (the raw body
 * as the argument "plain", as the core gives it); the first route whose path and method match runs, else onNotFound
 * (else a 404). A response not sent by the handler is a 500, as the core's.
 */
#include "WebServer.h"
#include "rw_internal.h"

namespace {

const char* kB64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

String base64(const String& in) {
    String out;
    const unsigned char* p = reinterpret_cast<const unsigned char*>(in.c_str());
    unsigned int n = in.length();
    for (unsigned int i = 0; i < n; i += 3) {
        uint32_t v = (uint32_t)p[i] << 16 | (i + 1 < n ? (uint32_t)p[i + 1] << 8 : 0) | (i + 2 < n ? p[i + 2] : 0);
        out += kB64[(v >> 18) & 63];
        out += kB64[(v >> 12) & 63];
        out += i + 1 < n ? kB64[(v >> 6) & 63] : '=';
        out += i + 2 < n ? kB64[v & 63] : '=';
    }

    return out;
}

HTTPMethod method_of(const String& m) {
    if (m == "GET") return HTTP_GET;
    if (m == "POST") return HTTP_POST;
    if (m == "PUT") return HTTP_PUT;
    if (m == "DELETE") return HTTP_DELETE;
    if (m == "PATCH") return HTTP_PATCH;
    if (m == "HEAD") return HTTP_HEAD;
    if (m == "OPTIONS") return HTTP_OPTIONS;
    return HTTP_ANY;
}

}  // namespace

WebServer::WebServer(int port) : _server((uint16_t)port) {}
WebServer::WebServer(IPAddress addr, int port) : _server((uint16_t)port) { (void)addr; }
WebServer::~WebServer() { close(); }

void WebServer::begin() { _server.begin(); }
void WebServer::begin(uint16_t port) { _server.begin(port); }

void WebServer::close() {
    _server.end();
    if (_client) _client.stop();
}

void WebServer::on(const String& uri, HTTPMethod method, THandlerFunction fn) { _routes.push_back(Route{uri, method, fn}); }

String WebServer::urlDecode(const String& text) {
    String out;
    out.reserve(text.length());
    for (unsigned int i = 0; i < text.length(); i++) {
        char c = text.charAt(i);
        if (c == '+') out += ' ';
        else if (c == '%' && i + 2 < text.length()) {
            char hex[3] = {text.charAt(i + 1), text.charAt(i + 2), 0};
            out += (char)strtol(hex, nullptr, 16);
            i += 2;
        } else out += c;
    }

    return out;
}

String WebServer::responseCodeToString(int code) {
    switch (code) {
    case 100: return "Continue";
    case 200: return "OK";
    case 201: return "Created";
    case 202: return "Accepted";
    case 204: return "No Content";
    case 301: return "Moved Permanently";
    case 302: return "Found";
    case 303: return "See Other";
    case 304: return "Not Modified";
    case 307: return "Temporary Redirect";
    case 400: return "Bad Request";
    case 401: return "Unauthorized";
    case 403: return "Forbidden";
    case 404: return "Not Found";
    case 405: return "Method Not Allowed";
    case 408: return "Request Time-out";
    case 413: return "Request Entity Too Large";
    case 500: return "Internal Server Error";
    case 501: return "Not Implemented";
    case 503: return "Service Unavailable";
    default: return "";
    }
}

void WebServer::parseArguments(const String& data) {
    unsigned int pos = 0;
    while (pos < data.length() && _argCount < WEBSERVER_MAX_ARGS) {
        int amp = data.indexOf('&', pos);
        String pair = data.substring(pos, amp < 0 ? data.length() : (unsigned int)amp);
        pos = amp < 0 ? data.length() : (unsigned int)amp + 1;
        if (pair.length() == 0) continue;
        int eq = pair.indexOf('=');
        _argNames[_argCount] = urlDecode(eq < 0 ? pair : pair.substring(0, (unsigned int)eq));
        _argValues[_argCount] = eq < 0 ? String("") : urlDecode(pair.substring((unsigned int)eq + 1));
        _argCount++;
    }
}

/* The request: its line, its headers, its body; false when none came in time. */
bool WebServer::readRequest() {
    uint64_t deadline = rw::now_ns() + (uint64_t)HTTP_MAX_DATA_WAIT * 1000000ull;
    String head;
    int ended = -1;
    while (ended < 0) {
        while (_client.available()) {
            head += (char)_client.read();
            if (head.length() >= 4 && head.endsWith("\r\n\r\n")) {
                ended = (int)head.length();
                break;
            }
        }

        if (ended >= 0) break;
        if (!_client.connected() || rw::now_ns() > deadline) return false;
        delay(1);
    }

    int lineEnd = head.indexOf("\r\n");
    String line = head.substring(0, (unsigned int)lineEnd);
    int sp1 = line.indexOf(' '), sp2 = line.indexOf(' ', (unsigned int)sp1 + 1);
    if (sp1 < 0 || sp2 < 0) return false;
    _method = method_of(line.substring(0, (unsigned int)sp1));
    String target = line.substring((unsigned int)sp1 + 1, (unsigned int)sp2);
    int q = target.indexOf('?');
    _uri = urlDecode(q < 0 ? target : target.substring(0, (unsigned int)q));
    _argCount = 0;
    if (q >= 0) parseArguments(target.substring((unsigned int)q + 1));

    _headerCount = 0;
    size_t bodyLength = 0;
    String contentType;
    unsigned int pos = (unsigned int)lineEnd + 2;
    while (pos < head.length()) {
        int e = head.indexOf("\r\n", pos);
        if (e < 0 || (unsigned int)e == pos) break;
        String h = head.substring(pos, (unsigned int)e);
        pos = (unsigned int)e + 2;
        int colon = h.indexOf(':');
        if (colon < 0) continue;
        String name = h.substring(0, (unsigned int)colon), value = h.substring((unsigned int)colon + 1);
        value.trim();
        if (name.equalsIgnoreCase("Content-Length")) bodyLength = (size_t)value.toInt();
        if (name.equalsIgnoreCase("Content-Type")) contentType = value;
        if (_headerCount < WEBSERVER_MAX_HEADERS) {
            _headerNames[_headerCount] = name;
            _headerValues[_headerCount] = value;
            _headerCount++;
        }
    }

    if (bodyLength > 0) {
        String body;
        body.reserve((unsigned int)bodyLength);
        while (body.length() < bodyLength) {
            while (_client.available() && body.length() < bodyLength) body += (char)_client.read();
            if (body.length() >= bodyLength) break;
            if (!_client.connected() || rw::now_ns() > deadline) break;
            delay(1);
        }

        if (contentType.startsWith("application/x-www-form-urlencoded")) parseArguments(body);
        if (_argCount < WEBSERVER_MAX_ARGS) {
            _argNames[_argCount] = "plain";   // the raw body, as the core gives it
            _argValues[_argCount] = body;
            _argCount++;
        }
    }

    return true;
}

void WebServer::handleClient() {
    rw::charge(rw::cost(2000.0));
    if (!_client || !_client.connected()) {
        _client = _server.accept();
        if (!_client) return;
    }

    if (!readRequest()) {
        _client.stop();
        return;
    }

    _extraHeaders = String("");
    _contentLength = CONTENT_LENGTH_NOT_SET;
    _chunked = false;
    _responded = false;
    bool handled = false;
    for (auto& r : _routes) {
        if (r.uri != _uri || (r.method != HTTP_ANY && r.method != _method)) continue;
        if (r.fn) r.fn();
        handled = true;
        break;
    }

    if (!handled) {
        if (_notFound) _notFound();
        else send(404, "text/plain", String("Not found: ") + _uri);
    }

    if (!_responded) send(500, "text/plain", "Server Error");   // a handler that sent nothing
    finishResponse();
    _client.stop();
}

void WebServer::sendHeader(const String& name, const String& value, bool first) {
    String line = name + ": " + value + "\r\n";
    _extraHeaders = first ? line + _extraHeaders : _extraHeaders + line;
}

void WebServer::send(int code, const char* content_type, const String& content) {
    _responded = true;
    String head = String("HTTP/1.1 ") + String(code) + " " + responseCodeToString(code) + "\r\n";
    if (content_type && *content_type) head += String("Content-Type: ") + content_type + "\r\n";
    if (_cors) head += "Access-Control-Allow-Origin: *\r\n";
    _chunked = _contentLength == CONTENT_LENGTH_UNKNOWN;
    if (_chunked) head += "Transfer-Encoding: chunked\r\n";
    else head += String("Content-Length: ") + String((unsigned long)(_contentLength == CONTENT_LENGTH_NOT_SET ? content.length() : _contentLength)) + "\r\n";
    head += _extraHeaders;
    head += "Connection: close\r\n\r\n";
    _client.write(reinterpret_cast<const uint8_t*>(head.c_str()), head.length());
    if (content.length() > 0 && _method != HTTP_HEAD) sendContent(content);
}

void WebServer::sendContent(const char* content, size_t size) {
    if (size == 0) return;
    if (_chunked) {
        char len[12];
        snprintf(len, sizeof len, "%x\r\n", (unsigned)size);
        _client.write(reinterpret_cast<const uint8_t*>(len), strlen(len));
    }

    _client.write(reinterpret_cast<const uint8_t*>(content), size);
    if (_chunked) _client.write(reinterpret_cast<const uint8_t*>("\r\n"), 2);
}

void WebServer::finishResponse() {
    if (_chunked) _client.write(reinterpret_cast<const uint8_t*>("0\r\n\r\n"), 5);
    _chunked = false;
}

String WebServer::arg(const String& name) const {
    for (int i = 0; i < _argCount; i++)
        if (_argNames[i] == name) return _argValues[i];
    return String("");
}

String WebServer::arg(int i) const { return i >= 0 && i < _argCount ? _argValues[i] : String(""); }
String WebServer::argName(int i) const { return i >= 0 && i < _argCount ? _argNames[i] : String(""); }

bool WebServer::hasArg(const String& name) const {
    for (int i = 0; i < _argCount; i++)
        if (_argNames[i] == name) return true;
    return false;
}

void WebServer::collectHeaders(const char* headerKeys[], const size_t headerKeysCount) {
    (void)headerKeys;
    (void)headerKeysCount;   // every header is kept (up to WEBSERVER_MAX_HEADERS)
}

String WebServer::header(const String& name) const {
    for (int i = 0; i < _headerCount; i++)
        if (_headerNames[i].equalsIgnoreCase(name)) return _headerValues[i];
    return String("");
}

String WebServer::header(int i) const { return i >= 0 && i < _headerCount ? _headerValues[i] : String(""); }
String WebServer::headerName(int i) const { return i >= 0 && i < _headerCount ? _headerNames[i] : String(""); }

bool WebServer::hasHeader(const String& name) const {
    for (int i = 0; i < _headerCount; i++)
        if (_headerNames[i].equalsIgnoreCase(name)) return true;
    return false;
}

bool WebServer::authenticate(const char* username, const char* password) {
    String auth = header("Authorization");
    if (!auth.startsWith("Basic ")) return false;
    String given = auth.substring(6);
    given.trim();
    return given == base64(String(username) + ":" + password);
}

void WebServer::requestAuthentication(HTTPAuthMethod mode, const char* realm, const String& authFailMsg) {
    (void)mode;   // Basic: a digest needs MD5 the fast mode does not do here
    sendHeader("WWW-Authenticate", String("Basic realm=\"") + (realm ? realm : "Login Required") + "\"");
    send(401, "text/html", authFailMsg);
}
