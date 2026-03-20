local players = {}
local matches = {}
local winConditions = {
    {1, 2, 3},  -- First row
    {4, 5, 6},  -- Second row  
    {7, 8, 9},  -- Third row
    {1, 4, 7},  -- Left column
    {2, 5, 8},  -- Middle column
    {3, 6, 9},  -- Right column
    {1, 5, 9},  -- Diagonal top left to bottom right
    {3, 5, 7}   -- Diagonal bottom left to top right
}

function checkWin(values, sign)
    for _, condition in ipairs(winConditions) do
        if (values[condition[1]] == sign) and (values[condition[2]] == sign) and (values[condition[3]] == sign) then
            return sign
        end
    end
    return false
end

function onClientSentData(client, jsonData)
    if (client == nil or jsonData == nil or (jsonData ~= nil and jsonData == "")) then
        return false
    end
    data = fromJSON(jsonData)
    if (data["type"]) then
        if (data["type"] == "quickmatch") then
            if (#players%2 == 1) then -- if it's equal to 1, means a player is waiting for a match!
                local sessionID = getSessionID(client)
                local plr1 = getClientFromSessionID(players[#players])
                matches[#matches]["players"] = {players[#players], sessionID}
                players[#players+1] = sessionID

                local value = {
                    ["type"] = "matchFound",
                    ["sign"] = "X",
                    ["values"] = {"", "", "", "", "", "", "", "", ""},
                    ["gameID"] = #matches
                }
                sendTCPToClient(plr1, toJSON(value))
                
                -- Client is second player
                value["sign"] = "O"
                sendTCPToClient(client, toJSON(value))
            else -- equal 0, he'll have to wait for a match!
                local sessionID = getSessionID(client)
                local match = {["players"] = {sessionID}, ["values"] = {"", "", "", "", "", "", "", "", ""}}
                players[#players+1] = sessionID
                matches[#matches+1] = match
            end
        elseif (data["type"] == "takeOver") then
            local gameID = data["gameID"]
            local pos = data["position"]
            local sign = data["sign"]
            local sessionID = getSessionID(client)
            local plr1 = getClientFromSessionID(matches[gameID]["players"][1])
            local plr2 = getClientFromSessionID(matches[gameID]["players"][2])
            local values = matches[gameID]["values"]

            values[pos] = sign

            local winner = checkWin(values, "X") or checkWin(values, "O")
            if (winner) then
                local value = {
                    ["type"] = "gameover",
                    ["winner"] = winner
                }
                local jsonVal = toJSON(value)
                sendTCPToClient(plr1, jsonVal)
                sendTCPToClient(plr2, jsonVal)
                return true -- game ended.
            end

            local value = {
                ["type"] = "yourturn",
                ["values"] = values,
            }
            local jsonVal = toJSON(value)
            if (matches[gameID]["players"][1] == sessionID) then
                sendTCPToClient(plr2, jsonVal) -- Only update the enemy, the player should be added in client side.
            else
                sendTCPToClient(plr1, jsonVal)
            end
        end
    else
        return false
    end
end
addEventHandler("onClientSentData", onClientSentData)