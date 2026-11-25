--#define LEVEL 3
--#if LEVEL > 2
print("high")
--#elif LEVEL > 1
print("mid")
--#else
print("low")
--#endif
--#if LEVEL == 1
one
--#elif LEVEL == 3
three
--#elif LEVEL == 3
three again
--#else
other
--#endif
local n = --#eval LEVEL * 2 + 1
local m = --[[# --#eval (LEVEL + 1) % 3
]]
--#if defined(LEVEL) && !defined(NOPE)
both
--#endif
--#if "abc" == "abc"
streq
--#endif
--#ifeq LEVEL 3
ifeq-yes
--#endif
--#ifneq LEVEL 3
ifneq-yes
--#endif
