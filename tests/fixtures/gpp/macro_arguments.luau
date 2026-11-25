--#define SQ(x) x * x
--#define CALL(f) f(1, 2)
--#define NUM(a,b) #1 - #2 = #2 - #1
print(SQ(5))
print(CALL(print))
print(NUM(10, 20))
local t = { SQ(2), SQ((1+1)) }
--#define EMPTY
a EMPTY b EMPTY(x) c
--#define ALIAS print
ALIAS("hi")
--#undef SQ
print(SQ(5))
--#define NESTED(a) [SQ2(a)]
--#define SQ2(v) (v^2)
NESTED(7)
--#defeval FROZEN LEVEL
--#define LEVEL 9
FROZEN LEVEL
--#file
