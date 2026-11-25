--#define FROM_OUTER outer-value
start
--#include sub/inner.luau
FROM_INNER
--#sinclude sub/missing.luau
done
