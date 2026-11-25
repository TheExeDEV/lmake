--!strict
local Config = require("@shared/config")
local Signal = require("@pkg/signal")
local Promise = require "@pkg/promise"
local Math = require('@UTILS/math')
local Client = require("@self/client")
local Missing = require("@nope/thing")
--#ifdef DEBUG
print("version", VERSION)
--#endif
return { Config = Config, Signal = Signal, Promise = Promise, Math = Math, Client = Client, Missing = Missing }
