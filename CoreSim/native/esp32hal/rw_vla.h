/* A variable-length array: GCC's extension (`uint8_t buf[len];`, len known only at run time), which the ESP32's
 * compiler takes and MSVC does not. Where MSVC reports one (C2131), the fast build rewrites the declaration into one of
 * these, in a copy of the file: it frees itself at the end of its scope as the array would, indexes and decays to a pointer
 * as the array does; sizeof differs, so a file that takes the array's sizeof is left as it is. */
#ifndef RW_VLA_H
#define RW_VLA_H
#ifdef __cplusplus
#include <stddef.h>

namespace rw {
template <typename T>
class vla {
public:
    explicit vla(size_t n) : p_(new T[n ? n : 1]) {}
    ~vla() { delete[] p_; }
    vla(const vla&) = delete;
    vla& operator=(const vla&) = delete;
    operator T*() const { return p_; }

private:
    T* p_;
};
}  // namespace rw
#endif
#endif
