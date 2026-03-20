local allData = {}
local mlCon = newMLContext(41)
local csvFile = LoadCsv(mlCon, "UnemploymentRate.csv")

for i, v in ipairs(csvFile) do
    table.insert(allData, {
        ["Year"] = v["Year"], 
        ["Month"] = v["Month"],
        ["State"] = v["State/Area"],
        ["UnemploymentRate"]= tonumber(v["UnemploymentRate"])
    })
end

function onClientSentData(client, jsonData)
    if (client == nil or jsonData == nil or jsonData == "") then return false end
    content = fromJSON(jsonData)
    if (content.title ~= "AI") then return false end
    if (#content.content == 0) then return false end
    values = splitString(content.content)
    if (#values>2) then
        result = "Format error! Format:\nstate year"
        message = toJSON({["data"] = result})
        sendTCPToClient(client, message)
        print("Error occured. Wrong format!")
        return false
    end
    values[2] = tonumber(values[2])
    result = forecast(values[1], values[2])
    message = toJSON({["data"] = result})
    sendTCPToClient(client, message)
end
addEventHandler("onClientSentData", onClientSentData)

function splitString(text)
    result = {}
    for str in string.gmatch(text, "[^|]+") do -- Regular Expression, "[^|]+" = all |
        table.insert(result, str)
    end
    return result
end

function mergeMixedTables(t1, t2)
    for k, v in pairs(t2) do
        if type(k) == "number" then
            -- numeric index: make sure t1[k] exists as a table
            t1[k] = t1[k] or {}
            for nk, nv in pairs(v) do
                t1[k][nk] = nv
            end
        else
            -- string key: just overwrite or add
            t1[k] = v
        end
    end
    return t1
end

function getDataBetweenYears(data, year)
    local result = {}
    for i, v in ipairs(data) do
        if (v["Year"] > 2000 and v["Year"] <= year) then
            table.insert(result, {
                ["Year"]=v["Year"], 
                ["Month"]=v["Month"],
                ["UnemploymentRate"]=tonumber(v["UnemploymentRate"])
            })
        end
    end
    return result
end

function TrainAndForecast(mlContext, data, windowSize, seriesLength, horizonValue, confidence)
    return CS_TrainAndForecast(mlContext, data, windowSize, seriesLength, horizonValue, confidence)
end

function CalculateHistoricalAverage(data, targetYear)
    if #data == 0 then return {} end

    local monthlySums = {}
    local monthlyCounts = {}
    for i = 1, 12 do
        monthlySums[i] = 0
        monthlyCounts[i] = 0
    end

    -- Filter data: only include rows with Year < targetYear
    local filteredData = {}
    for _, point in ipairs(data) do
        if point["Year"] < targetYear then
            table.insert(filteredData, point)
        end
    end

    if #filteredData == 0 then return {} end

    -- Find the last year in filtered data
    local lastYear = filteredData[#filteredData]["Year"]

    -- Take last 3 years for averaging
    for _, point in ipairs(filteredData) do
        if point["Year"] >= (lastYear - 2) then  -- last 3 years
            local m = point["Month"]
            monthlySums[m] = monthlySums[m] + point["UnemploymentRate"]
            monthlyCounts[m] = monthlyCounts[m] + 1
        end
    end

    -- Calculate monthly averages
    local monthlyAverages = {}
    for i = 1, 12 do
        if monthlyCounts[i] > 0 then
            monthlyAverages[i] = monthlySums[i] / monthlyCounts[i]
        else
            monthlyAverages[i] = 0
        end
    end

    -- Build 12-month forecast starting from Jan of targetYear
    local forecast = {}
    for i = 1, 12 do
        forecast[i] = monthlyAverages[i]
    end

    return forecast
end

function DisplayEnsembleForecast(historicalData, forecasts, targetYear)
    local result = {}
    local avg = {}

    local lastRate = historicalData[#historicalData]["UnemploymentRate"]

    local function monthName(m)
        local names = {
            "Jan","Feb","Mar","Apr","May","Jun",
            "Jul","Aug","Sep","Oct","Nov","Dec"
        }
        return names[m]
    end

    -- Forecast starting at January of targetYear
    local prevYear = targetYear - 1
    local prevYearByMonth = {}

    for _, v in ipairs(historicalData) do
        if tonumber(v["Year"]) == prevYear then
            prevYearByMonth[tonumber(v["Month"])] = tonumber(v["UnemploymentRate"])
        end
    end

    local startMonth = 1
    for i = 1, 12 do
        result[i] = {}
        local fMonth = ((startMonth + i - 2) % 12) + 1
        local fYear = targetYear + math.floor((startMonth + i - 2) / 12)
        local forecastDate = monthName(fMonth) .. " " .. tostring(fYear)

        local pred1 = forecasts["SSA Seasonal"][i]
        local pred2 = forecasts["SSA Medium"][i]
        local pred3 = forecasts["Historical Avg"][i]

        local ensemble = 0.5*pred2 + 0.3*pred1 + 0.2*pred3
        table.insert(avg, ensemble)
        

        local trend = "N/A"
        local prevYearValue = prevYearByMonth[fMonth]
        if (prevYearValue) then
            if ensemble > prevYearValue then
                trend = "Increase"
            elseif ensemble < prevYearValue then
                trend = "Decrease"
            else
                trend = "Same"
            end
        end
        result[i]["forecastDate"] = forecastDate
        result[i]["SSASeasonal"] = pred1
        result[i]["SSAMedium"] = pred2
        result[i]["Historical"] = pred3
        result[i]["Ensemble"] = ensemble
        result[i]["PrevYearEnsemble"] = prevYearValue
    end

    -- ===== SUMMARY =====
    local ensembleValues = {}
    for i = 1, 12 do
        local val = (forecasts["SSA Seasonal"][i] +
                     forecasts["SSA Medium"][i] +
                     forecasts["Historical Avg"][i]) / 3
        table.insert(ensembleValues, val)
    end

    local function average(t)
        local sum = 0
        for _, v in ipairs(t) do sum = sum + v end
        return sum / #t
    end

    local function minMax(t)
        local minV = t[1]
        local maxV = t[1]
        for _, v in ipairs(t) do
            if v < minV then minV = v end
            if v > maxV then maxV = v end
        end
        return minV, maxV
    end

    local avgRate = average(ensembleValues)
    local minV, maxV = minMax(ensembleValues)
    --=== ENSEMBLE FORECAST SUMMARY ===
    result["Average"] = avgRate
    result["Range1"] = minV
    result["Range2"] = maxV
    result["Mostlikely"] = CalculateMode(ensembleValues)

    -- ===== INTERPRETATION =====
    if avgRate < 4.0 then
        result["interpretation"] = "LABOR MARKET: Very strong (below natural unemployment)"
    elseif avgRate < 5.0 then
        result["interpretation"] = "LABOR MARKET: Healthy (typical recovery period)"
    elseif avgRate < 6.0 then
        result["interpretation"] = "LABOR MARKET: Moderate (watch for trends)"
    else
        result["interpretation"] = "LABOR MARKET: Concerning (above historical average)"
    end

    -- ===== POLICY IMPLICATIONS =====
    if avgRate >= 6.0 then
        result["policyImplications"] = "Severe unemployment risk - aggressive stimulus recommended"
    elseif avgRate >= 5.0 then
        result["policyImplications"] = "Elevated unemployment - targeted job programs advised"
    elseif avgRate >= 4.0 then
        result["policyImplications"] = "Moderate labor market - careful economic monitoring"
    else
        result["policyImplications"] = "Strong labor market - no immediate intervention required"
    end

    result["policyImplicationsSpecial"] = "false"
    if math.abs(ensembleValues[#ensembleValues] - ensembleValues[1]) > 0.75 then
        result["policyImplicationsSpecial"] = "Noticeable trend shift detected - monitor momentum closely\n"
    end

    return result, avg
end

function TryCompareWithActuals(historicalData, targetYear, avg)
    local result = {}

    -- Filter actual values for the given year, only 1 row per month
    local actualValues = {}
    local monthsValues = {}
    monthsValues["pastYearValues"] = {}
    for _, d in ipairs(historicalData) do
        if tonumber(d["Year"]) == targetYear then
            local month = tonumber(d["Month"])
            actualValues[month] = d  -- store by month index
        elseif (tonumber(d["Year"]) >= (targetYear-3)) and (tonumber(d["Year"]) < targetYear) then
            local month, year = d["Month"], d["Year"]
            monthsValues["pastYearValues"][math.floor(year)..math.floor(month)] = d["UnemploymentRate"]
        end
    end

    -- Only continue if there is at least one actual value
    if next(actualValues) == nil then return result, monthsValues end

    local monthNames = {
        "Jan","Feb","Mar","Apr","May","Jun",
        "Jul","Aug","Sep","Oct","Nov","Dec"
    }

    for month = 1, 12 do
        result[month] = {}
        local actual = actualValues[month]

        if actual then
            local ensemble = avg[month]
            local unemploymentRate = tonumber(actual["UnemploymentRate"])
            local error = math.abs(ensemble - actual["UnemploymentRate"])
            local errorPercent = actual["UnemploymentRate"] ~= 0 and (error / actual["UnemploymentRate"]) * 100 or 0
            local dateStr = monthNames[month] .. " " .. tostring(targetYear)
            if (targetYear == 2025 and month > 9) then
                ensemble = -1
                error = -1
                errorPercent = -1
                unemploymentRate = -1
            end
            result[month]["compareDate"] = dateStr
            result[month]["compareEnsemble"] = ensemble
            result[month]["compareUnemploymentRate"] = unemploymentRate
            result[month]["compareError"] = error
            result[month]["compareErrorPercent"] = errorPercent
        end
    end
    return result, monthsValues
end

function CalculateMode(values)
    local counts = {}

    for _, v in ipairs(values) do
        local rounded = math.floor(v * 10 + 0.5) / 10  -- round to 1 decimal
        counts[rounded] = (counts[rounded] or 0) + 1
    end

    -- Find the mode
    local mode = nil
    local maxCount = 0

    for value, count in pairs(counts) do
        if count > maxCount then
            maxCount = count
            mode = value
        end
    end

    -- If mode exists, return it
    if mode ~= nil then
        return mode
    end

    -- Fallback: return average
    local sum = 0
    for _, v in ipairs(values) do
        sum = sum + v
    end

    return sum / #values
end

function forecast(state, targetYear)
    local result = {}
    targetYear = tonumber(targetYear)
    local timeSeriesData = {}
    for i, v in ipairs(allData) do
        if (string.lower(v["State"]) == string.lower(state)) then
            table.insert(timeSeriesData, {
                ["Year"] = v["Year"], 
                ["Month"] = v["Month"],
                ["UnemploymentRate"]= v["UnemploymentRate"]
            })
        end
    end

    if (#timeSeriesData < 60) then -- Need at least 5 years to predict (12 month * 5 = 60)
        print("Insufficient data!")
        return false
    end
    local mlMedium = newMLContext(99)
    local recentData = getDataBetweenYears(timeSeriesData, targetYear)
    table.sort(recentData, function(a, b)
        if a["Year"] == b["Year"] then
            return a["Month"] < b["Month"]
        else
            return a["Year"] < b["Year"]
        end
    end)
    if (targetYear > 2025) then -- Months 10,11,12 have not been published yet at LAUS Home : U.S. Bureau of Labor Statistics
        local inputs = {}
        for i, v in pairs(recentData) do
            if (v["Year"] >= targetYear-4) then
                table.insert(inputs, v["UnemploymentRate"])
            end
        end

        local fore1 = TrainAndForecast(mlCon, inputs, 12, 48, 3, 0.58)
        local fore2 = TrainAndForecast(mlMedium, inputs, 6, 36, 3, 0.58)

        for i = 1, 3 do
            table.insert(recentData, {
                ["Year"]=2025,
                ["Month"]=(12-(3-i)),
                ["UnemploymentRate"]=(fore1[i] + fore2[i]) / 2,
            })
        end
    end

    local inputSeries = {}
    for i, v in pairs(recentData) do
        if (v["Year"] < targetYear) then
            table.insert(inputSeries, v["UnemploymentRate"])
        end
    end

    -- Forecast using 3 different models:
    local forecasts = {}

    -- Training Seasonal SSA with tighter parameters
    local forecast1 = TrainAndForecast(mlCon, inputSeries, 12, 48, 12, 0.58)
    forecasts["SSA Seasonal"] = forecast1

    -- Training Medium term SSA with different parameters
    local forecast2 = TrainAndForecast(mlMedium, inputSeries, 6, 36, 12, 0.58)
    forecasts["SSA Medium"] = forecast2

    -- Calculating forecast simple historical average of the last 3 years
    local forecast3 = CalculateHistoricalAverage(recentData, targetYear)
    forecasts["Historical Avg"] = forecast3

    -- Display each model forcast
    local resEn, avg = DisplayEnsembleForecast(recentData, forecasts, targetYear)
    if (resEn ~= nil and #resEn>0) then
        result = mergeMixedTables(result, resEn)
    end

    result["pastYearValues"] = {}
    -- 4. Show actual vs predicted for known data (if available)
    local compareRes, monthsValues = TryCompareWithActuals(recentData, targetYear, avg)
    if (#compareRes>0) then
        result["comparison"] = true
        result = mergeMixedTables(result, compareRes)
    else
        result["comparison"] = false
    end

    result = mergeMixedTables(result, monthsValues)
    result["targetYear"] = targetYear
    result["state"] = state

    return result
end