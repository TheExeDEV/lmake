local function f(...)
	return select("#", ...)
end
local t = {1, 2, 3}
print(#t, #"abc")
local s = [[long string]]
--#define T true
if T then print(`interp {T}`) end
