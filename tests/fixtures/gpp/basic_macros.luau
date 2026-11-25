--#define DEBUG
--#define VERSION "1.2.3"
--#define ADD(a, b) ((a) + (b))
local version = VERSION
local x = ADD(1, 2)
--#ifdef DEBUG
print("debug build", #arr, t[#t])
--#else
print("release")
--#endif
--#ifndef DEBUG
local nope = true
--#endif
local y = --[[# ADD(3, 4) ]]
local s = "--[[#VERSION]]"
