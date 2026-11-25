--!strict
local Config = require("@self/shared/config")
local Signal = require("./packages/signal")
local Promise = require "./packages/promise"
local Math = require('@self/shared/utils/math')
local Client = require("@self/client")
local Missing = require("@nope/thing")
print("version", "1.0.0")
return { Config = Config, Signal = Signal, Promise = Promise, Math = Math, Client = Client, Missing = Missing }
