/**
 *  \brief Stack checker extension utility file.
 *
 *  This file has been automatically added to your project by the Stack Checker
 *  Atmel Studio extension. If you are not currently using this extension, this
 *  file may be removed from your project.
 */

#include <stdint.h>

#ifndef __GNUC__
#  error The stack instrumentation code is designed for GCC toolchains only.
#endif

#ifndef __AVR__
#  error The stack instumentation code is intended for AVR 8-bit targets only.
#endif

/** Linker provided symbols for the end of the static data section, and the
 *  of the stack.
 */
extern void *_end, *__stack;

/** \internal
 *  \brief Low level stack painting function, hooked into the avr-libc initialization code.
 *
 *  Paints the internal SRAM between the end of the static data and the start
 *  of the stack with a known \c 0xDEADBEEF hex pattern. This is then detected
 *  by the Stack Checker extension when a debug session is halted to determine
 *  the maximum stack usage of the running application.
 */
void _StackPaint(void) __attribute__((naked)) __attribute__((section (".init1")));
void _StackPaint(void)
{
	const uint8_t fill_pattern[] = {0xDE, 0xAD, 0xBE, 0xEF};
	uint8_t *fill_start = (uint8_t*)&_end;
	uint8_t *fill_end = (uint8_t*)&__stack;

	for (uint8_t* fill_pos = fill_start; fill < fill_end; fill++)
	{
		*fill_pos = fill_pattern[(uintptr_t)fill & 0x03];
	}
}
