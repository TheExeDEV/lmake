--!strict
--#define PLATFORM server
--#define VERSION_MAJOR 2
--#define VERSION_MINOR 14
--#define ASSERT(cond, msg) if not (cond) then error(msg, 2) end
--#define LOG(level, a, b) print("[" .. level .. "]", a, b)
local Players = game:GetService("Players")
local Module = {}
Module.__index = Module

export type State = {
	count: number,
	names: { string },
}

function Module.new(count: number): State
	ASSERT(count >= 0, "count must be positive")
	local self = setmetatable({ count = count, names = {} }, Module)
	--#ifeq PLATFORM server
	self.isServer = true
	--#else
	self.isServer = false
	--#endif
	return self
end

function Module:describe(): string
	local version = `{--[[# VERSION_MAJOR ]]}.{--[[# VERSION_MINOR ]]}`
	--#if VERSION_MAJOR >= 2 && VERSION_MINOR > 10
	return string.format("v%s (%d items, %d names)", version, self.count, #self.names)
	--#elif VERSION_MAJOR == 1
	return "legacy"
	--#else
	return "unknown"
	--#endif
end

function Module:add(name: string)
	table.insert(self.names, name)
	LOG("info", name, #self.names)
end

for _, player in Players:GetPlayers() do
	print(player.Name, (function(a, b) return a + b end)(1, 2))
end

return Module
