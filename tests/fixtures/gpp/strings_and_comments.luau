--#define NAME world
print("hello NAME")
--#mode string "\"" "\""
print("hello NAME", NAME)
--#mode nostring "\""
print('hello NAME', "NAME")
--#mode comment "<!" "!>"
<! NAME is hidden !> NAME
--#mode string qqq "<<" ">>"
<<raw NAME>> NAME
--#mode string QQQ "{{" "}}"
{{eval NAME}}
