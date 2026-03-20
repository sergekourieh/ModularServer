local db = dbConnect("dbAccount.db")
if (not db) then
    print("AccountSystem: Couldn't connect to database.")
    return false
end
dbExec(db, "Create table if not exists accounts (username VARCHAR(50) UNIQUE, hashedPassword VARCHAR(32), failedLoginCount INT, lastLogin BIGINT, registrationDate BIGINT)")

function register(client, username, password)
    if (not client or not username or not password) then return false end --Add return reason invalid username.. invalid password
    if (#username > 50) then -- Username's size is set to 50 [VARCHAR(50)]
        sendTCPToClient(client, "Username length should be 50 or shorter.")
        return false
    end
    if (#password < 8 and password > 32) then -- Password's size is set to 32 [VARCHAR(32)]
        sendTCPToClient(client, "Password length must be minimum of 8.")
        return false
    end
    local checkValid = dbQuery(db, "SELECT `username` FROM `accounts` WHERE `username`=?", username)
    if (#checkValid > 1) then
        sendTCPToClient(client, "Username is unavailable")
        Server.print("ERROR: more than one username: '"..username.."' exists!")
        return false
    elseif (#checkValid > 0) then
        sendTCPToClient(client, "Username is unavailable")
        return false
    end
    local hashedPass = hashIt(password)
    local timestamp = getTick()
    dbExec(db, "INSERT INTO accounts (username, hashedPassword, failedLoginCount, lastLogin, registrationDate) VALUES (?, ?, ?, ?, ?)", username, hashedPass, 0, 0, timestamp)

    -- Check if Registration complete
    local checkValid = dbQuery(db, "Select `hashedPassword` FROM `accounts` WHERE `username`=?", username)
    if (#checkValid == 0) then
        sendTCPToClient(client, "We've experience a problem in registrating your account. Please contact an admin. {error code: 1]}")
        return false
    end
    if (#checkValid > 0) then
        local hashedPass2 = checkValid[1].hashedPassword
        if (not checkHash(password, hashedPass2)) then
            sendTCPToClient(client, "We've experience a problem in registrating your account. Please contact an admin. {error code: 2}")
            return false
        end
        sendTCPToClient(client, "Account has been successfuly registered")
        triggerEvent("onClientRegister", client, username) -- Trigger any event called (onClientRegister) and send paramters client and username!
        return true
    end
end

function login (client, username, password)
    if (not client or not username or not password) then return false end --Add return reason invalid username.. invalid password
    if (getClientData(client, "username")) then
        sendTCPToClient(client, "You're already logged in!")
        return false
    end
    local checkValid = dbQuery(db, "SELECT `username`, `hashedPassword` FROM `accounts` WHERE `username`=?", username)
    if (#checkValid < 1) then
        sendTCPToClient(client, "Invalid username/password 1")
        local failedValue = dbQuery(db, "SELECT failedLoginCount FROM accounts WHERE username=?", username)
        dbExec(db, "UPDATE accounts SET failedLoginCount=? WHERE username=?", failedValue[1].failedLoginCount, username)
        return false
    end
    local userVal = checkValid[1].username
    local hashPassVal = checkValid[1].hashedPassword
    if (not userVal or not hashPassVal) then
        sendTCPToClient(client, "Invalid username/password 2")
        dbExec(db, "UPDATE accounts SET failedLoginCount=? WHERE username=?", failedValue[1].failedLoginCount, username)
        return false
    end
    local result = checkHash(password, hashPassVal)
    if (not result) then
        sendTCPToClient(client, "Invalid username/password 3")
        dbExec(db, "UPDATE accounts SET failedLoginCount=? WHERE username=?", failedValue[1].failedLoginCount, username)
        return false
    end
    sendTCPToClient(client, "Login successfully")
    triggerEvent("onClientLogin", client, username) -- Trigger any event called (onClientLogin) and send paramters client and username!
    setClientData(client, "username", username)
    dbExec(db, "UPDATE accounts SET lastLogin=? WHERE username=?", getTick(), username)
    return result
end

function logout (client)
    if (not client) then return false end
    --Clean whatever data you have to clean
    setClientData(client, "username", nil)
    sendTCPToClient(client, "Logout successfully")
    triggerEvent("onClientLogout", client) -- Trigger any event called (onClientLogout) and send client as parameter!
    return true
end

function onClientSentData(client, jsonData)
    if (not client or not jsonData) then return false end
    local jsonD = fromJSON(jsonData)
    if (jsonD.createaccount) then
        if (not jsonD.createaccount.username or not jsonD.createaccount.password) then return false end
        return register(client, jsonD.createaccount.username, jsonD.createaccount.password)
    elseif (jsonD.login) then
        if (not jsonD.login.username or not jsonD.login.password) then return false end
        return login(client, jsonD.login.username, jsonD.login.password)
    elseif (jsonD.logout) then
        return logout(client)
    end
    return false
end
addEventHandler("onClientSentData", onClientSentData)

function onClientJoin(client)
    print("Client Joined")
end
addEventHandler("onClientJoin", onClientJoin)

function onClientDisconnect(client)
    local username = getClientData(client, "username")
    local allData = getAllClientData(client)
    print("Client disconnected")
end
addEventHandler("onClientDisconnect", onClientDisconnect)