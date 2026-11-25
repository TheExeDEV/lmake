--#define MAX(a,b) ((a) > (b) and (a) or (b))
local m = MAX(MAX(1, 2), MAX(3, f(4, 5)))
--#define STR(x) "x"
print(STR(hello))
--#define CAT(a,b) a##b
CAT(foo,bar)
--#define WITHNL(a) begin
--#define X 1
--#if X
  --#if 0
  inner-no
  --#else
  inner-yes
  --#endif
--#endif
--#warning this is a warning
