--!strict
local Config = require("./shared/config")
local Signal = require("../packages/signal")
local Math = require("./shared/utils/math")
local text = `{require("./shared/config").name}`
local fake = obj.require("@shared/config")
return { Config = Config, Signal = Signal, Math = Math, text = text, fake = fake }
