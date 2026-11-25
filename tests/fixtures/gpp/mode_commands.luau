--#mode quote "\\"
--#define A 1
x \A y A
--#mode string "'" "'"
'A inside' A
--#mode comment "/*" "*/"
/* A comment */ A
--#mode nocomment
--#mode save
--#mode user "$" "$" "{" "}{" "}$" "{" "}" "@" ""
$A$ A
--#define B <z>
$B$
--#mode restore
A B(q)
