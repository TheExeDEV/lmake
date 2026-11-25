--#define F() nothing here
--#define G(a) <a>
--#define H g
--#define LEN(t) #t
--#define SUM(a, b, c) a + b + c
F() F G(1) G((1, 2)) H(5) LEN(items) SUM(1, (2 * 3), f(4, 5))
--#define ID 42
x = --#eval length(abc) + ID
y = --#eval defined(ID) + defined(NOPE) * 10
z = --#eval 0x1F + 010 - -3
w = --#eval "a" < "b"
v = --#eval (1 + 2) * (3 - 4) / 2 % 5
u = --#eval 7 & 3 | 8 ^ 1
t = --#eval !0 && ~0
s = --#eval not a number
