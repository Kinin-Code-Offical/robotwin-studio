/* PROGMEM helpers: on the ESP32 (and here) flash is memory mapped, so these are plain reads. */
#ifndef RW_PGMSPACE_H
#define RW_PGMSPACE_H
#include <stdint.h>
#include <string.h>

#ifndef PROGMEM
#define PROGMEM
#endif
#define PGM_READ_TYPE(addr, type) (*(const type*)(const void*)(addr))
#define pgm_read_byte(addr) PGM_READ_TYPE(addr, uint8_t)
#define pgm_read_word(addr) PGM_READ_TYPE(addr, uint16_t)
#define pgm_read_dword(addr) PGM_READ_TYPE(addr, uint32_t)
#define pgm_read_float(addr) PGM_READ_TYPE(addr, float)
#define pgm_read_ptr(addr) PGM_READ_TYPE(addr, void*)
#define pgm_read_byte_near(addr) pgm_read_byte(addr)
#define pgm_read_word_near(addr) pgm_read_word(addr)
#define pgm_read_dword_near(addr) pgm_read_dword(addr)
#define pgm_read_float_near(addr) pgm_read_float(addr)
#define pgm_read_ptr_near(addr) pgm_read_ptr(addr)
#define pgm_read_byte_far(addr) pgm_read_byte(addr)
#define pgm_read_word_far(addr) pgm_read_word(addr)
#define pgm_read_dword_far(addr) pgm_read_dword(addr)
#define memcpy_P memcpy
#define memcmp_P memcmp
#define memccpy_P memccpy
#define memmem_P memmem
#define strcpy_P strcpy
#define strncpy_P strncpy
#define strcat_P strcat
#define strncat_P strncat
#define strcmp_P strcmp
#define strncmp_P strncmp
#define strcasecmp_P _stricmp
#define strncasecmp_P _strnicmp
#define strlen_P strlen
#define strnlen_P strnlen
#define strstr_P strstr
#define sprintf_P sprintf
#define snprintf_P snprintf
#define vsnprintf_P vsnprintf
#define printf_P printf
#ifndef strcasecmp
#define strcasecmp _stricmp
#endif
#ifndef strncasecmp
#define strncasecmp _strnicmp
#endif
#endif
