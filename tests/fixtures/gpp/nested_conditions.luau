--#define A 1
--#ifeq   A    1
eq-trimmed
--#endif
--#ifneq A 2
neq
--#endif
--#if 0
--#define HIDDEN 1
--#undef A
--#endif
--#ifdef HIDDEN
bad
--#else
hidden-ok A
--#endif
--#defeval TWICE A A
TWICE
--#if 1
  --#if 0
  --#elif 1
  elif-taken
  --#elif 1
  elif-not-taken
  --#else
  else-not-taken
  --#endif
--#elif 1
outer-elif-bad
--#endif
