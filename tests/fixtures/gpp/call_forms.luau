--#define DOUBLE(x) (x) * 2
--#define WRAP(f) function(...) return f(...) end
local a = DOUBLE(DOUBLE(3))
local b = WRAP(print)
local c = DOUBLE( 5 )
local s = [[ DOUBLE(1) inside long string ]]
local t = { DOUBLE(1), DOUBLE(2) }
local unbalanced = DOUBLE
local spaced = DOUBLE (7)
