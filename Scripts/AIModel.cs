using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;
using NLua;

namespace Server {
    public class SeriesData {
        public float Value { get; set; }
    }
    public class ForecastResult {
        [ColumnName("Forecast")]
        public float[] ForecastedRates { get; set; }
        
        [ColumnName("LowerBound")]
        public float[] LowerBounds { get; set; }
        
        [ColumnName("UpperBound")]
        public float[] UpperBounds { get; set; }
    }
    
    public static class TimeSeriesForecaster {

        public static List<SeriesData> LuaTableToSeries(LuaTable table) {
            var list = new List<SeriesData>();
            foreach (var val in table.Values) {
                if (val == null) continue;
                list.Add(new SeriesData() {
                    Value = Convert.ToSingle(val)
                });
            }
            if (list.Count == 0)
                throw new Exception("There's no historical data.");

            return list;
        }
        public static LuaTable CS_TrainAndForecast(MLContext mlContext, Lua lua, LuaTable data, int windowSize, int seriesLength, int horizonValue, float confidence) {
            if (data == null)
                throw new Exception("There's no historical data.");
            List<SeriesData> list = LuaTableToSeries(data);

            var dataView = mlContext.Data.LoadFromEnumerable(list);

            var pipeline = mlContext.Forecasting.ForecastBySsa(
                outputColumnName: "Forecast",
                inputColumnName: nameof(SeriesData.Value),
                windowSize: windowSize,
                seriesLength: seriesLength,
                trainSize: list.Count,
                horizon: horizonValue,
                confidenceLevel: confidence,
                confidenceLowerBoundColumn: "LowerBound",
                confidenceUpperBoundColumn: "UpperBound"
            );

            var model = pipeline.Fit(dataView);
            var engine = model.CreateTimeSeriesEngine<SeriesData, ForecastResult>(mlContext);

            // 3. Predict next 12 months
            var forecast = engine.Predict();

            // Convert to LuaTable
            LuaTable luaTable = LuaMethodsEvents.CreateTable(lua);
            for (int i = 0; i < forecast.ForecastedRates.Length; i++) {
                luaTable[i + 1] = forecast.ForecastedRates[i];
            }

            return luaTable; // now returns the Lua table of 12 forecasts
        }
    }
}