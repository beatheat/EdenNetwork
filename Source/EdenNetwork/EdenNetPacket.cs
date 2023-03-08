using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EdenNetwork
{
    /// <summary>
    /// Struct : data type for represent error
    /// </summary>
    public struct EdenError
    {
        public string text;
        public EdenError(string text)
        {
            this.text = text;
        }
    }

    /// <summary>
    /// Struct : data type for represent single, dictionary, array form 
    /// </summary>
    public struct EdenData
    {
        /// <summary>
        /// Enum Type of EdenData
        /// </summary>
        public enum Type { SINGLE, ARRAY, DICTIONARY, ERROR }
        public Type type;
        public object? data;

        [JsonIgnore]
        private object[]? _arrayData = null;
        [JsonIgnore]
        private Dictionary<string, object>? _dictData = null;
        [JsonIgnore]
        public static readonly JsonSerializerOptions defaultOptions = new JsonSerializerOptions {IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};
        [JsonIgnore]
        private JsonSerializerOptions _options = defaultOptions;

        public EdenData()
        {
            this.data = null;
            type = Type.SINGLE;
        }

        public EdenData(EdenError error)
        {
            this.data = error;
            type = Type.ERROR;
        }

        /// <summary>
        /// Initialize structure by single data
        /// </summary>
        public EdenData(object? data)
        {
            this.data = data;
            type = Type.SINGLE;
        }
        /// <summary>
        /// Initialize structure by object array
        /// </summary>
        public EdenData(params object[] data)
        {
            this.data = data;
            type = Type.ARRAY;
        }
        /// <summary>
        /// Initialize structure by dictionary
        /// </summary>
        public EdenData(Dictionary<string, object> data)
        {
            this.data = data;
            type = Type.DICTIONARY;
        }
        /// <summary>
        /// object json string to each EdenData.Type
        /// </summary>
        public void CastJsonToType()
        {
            if (data == null) return;
            if (type == Type.ARRAY)
            {
                _arrayData = ParseData<object[]>(data, _options);
            }
            else if (type == Type.DICTIONARY)
            {
                _dictData = ParseData<Dictionary<string, object>>(data, _options);
            }
        }
        /// <summary>
        /// Check if data is error and get error message
        /// </summary>
        /// <returns>error message</returns>
        public bool CheckError(out string message)
        {
            message = "";
            if (type == Type.ERROR)
            {
                EdenError err = (EdenError)data!;
                message = err.text;
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Get single data
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <returns>parsed data for type desired</returns>
        public T? Get<T>()
        {
            if (type == Type.SINGLE)
            {
                if (data == null)
                    return default(T);
                return ParseData<T>(data, _options);
            }
            throw new Exception("EdenData::Get() - data is not single data");
        }
        
        /// <summary>
        /// Try get single value
        /// </summary>
        /// <param name="result">parsed data for type desired</param>
        /// <typeparam name="T">type to parse</typeparam>
        /// <returns>true if parse successfully</returns>
        public bool TryGet<T>(out T result)
        {
            result = default(T)!;
            if (type == Type.SINGLE && data != null)
            { 
                return TryParseData<T>(data, out result!, _options);
            }
            return false;
        }
        
        /// <summary>
        /// Get data by index from array object data
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="idx"> index of listed data </param>
        /// <returns>parsed data for type desired</returns>
        public T Get<T>(int idx)
        {
            if (_arrayData == null) throw new Exception("EdenData::Get(int idx) - data is null");
            if (type == Type.ARRAY)
            {
                if (idx < 0 || idx > _arrayData.Length)
                {
                    throw new Exception("EdenData::Get(int idx) - out of index ");
                }
                return ParseData<T>(_arrayData[idx], _options)!;
            }
            throw new Exception("EdenData::Get(int idx) - data is not array");
        }
        
        /// <summary>
        /// Try get data by index from array object data
        /// </summary>
        /// <param name="idx"> index of listed data </param>
        /// <param name="result">parsed data for type desired</param>
        /// <typeparam name="T">type to parse</typeparam>
        /// <returns>true if parse successfully</returns>
        public bool TryGet<T>(int idx, out T result)
        {
            result = default(T)!;
            if (type == Type.ARRAY && _arrayData != null)
            {
                if (idx < 0 || idx > _arrayData.Length)
                    return TryParseData<T>(_arrayData[idx], out result!, _options);
            }
            return false;
        }       
        
        /// <summary>
        /// Get data by key from dictionary data
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="key">key desire</param>
        /// <returns>parsed data for type desired</returns>
        public T Get<T>(string key)
        {
            if (_dictData == null) throw new Exception("EdenData::Get(string key) - data is null");
            if (type == Type.DICTIONARY)
            {
                if (_dictData.TryGetValue(key, out var value) == false)
                    throw new Exception("EdenData::Get(string tag) - there is no tag in data dictionary");
                return ParseData<T>(value, _options)!;
            }
            throw new Exception("EdenData::Get(int idx) - data is not dictionary");
        }
        
        /// <summary>
        /// Try get data by key from dictionary data
        /// </summary>
        /// <param name="key">key desire</param>
        /// <param name="result">parsed data for type desired</param>
        /// <typeparam name="T">type to parse</typeparam>
        /// <returns>true if parse successfully</returns>
        public bool TryGet<T>(string key, out T result)
        {
            result = default(T)!;
            if (type == Type.DICTIONARY && _dictData != null)
            {
                if (_dictData.TryGetValue(key, out var value))
                    return TryParseData<T>(value, out result!, _options);
            }
            return false;
        }
        
        
        /// <summary>
        /// Parse json data object to type desired
        /// </summary>f
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="data">data object</param>
        /// <returns>parsed data for type desired</returns>
        public static T? ParseData<T>(object data)
        {
            try
            {
                return ((JsonElement)data).Deserialize<T>(defaultOptions);
            }
            catch
            {
                throw new Exception("EdenData::ParseData - cannot parse data : \"" + data.ToString() + "\" to type : " + typeof(T).Name);
            }
        }

        /// <summary>
        /// Try parse json data object to type desired
        /// </summary>
        /// <param name="data">data object</param>
        /// <param name="result">parsed data for type desired</param>
        /// <typeparam name="T">type to parse</typeparam>
        /// <returns>true if parse successfully</returns>
        public static bool TryParseData<T>(object data, out T? result)
        {
            try
            {
                result = ((JsonElement) data).Deserialize<T>(defaultOptions);
                return true;
            }
            catch
            {
                result = default(T);
                return false;
            }
        }
        
        /// <summary>
        /// Parse json data object to type desired
        /// </summary>f
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="data">data object</param>
        /// <returns>parsed data for type desired</returns>
        public static T? ParseData<T>(object data, JsonSerializerOptions options)
        {
            try
            {
                return ((JsonElement)data).Deserialize<T>(options);
            }
            catch
            {
                throw new Exception("EdenData::ParseData - cannot parse data : \"" + data.ToString() + "\" to type : " + typeof(T).Name);
            }
        }

        /// <summary>
        /// Try parse json data object to type desired
        /// </summary>
        /// <param name="data">data object</param>
        /// <param name="result">parsed data for type desired</param>
        /// <typeparam name="T">type to parse</typeparam>
        /// <returns>true if parse successfully</returns>
        public static bool TryParseData<T>(object data, out T? result, JsonSerializerOptions options)
        {
            try
            {
                result = ((JsonElement) data).Deserialize<T>(options);
                return true;
            }
            catch
            {
                result = default(T);
                return false;
            }
        }


        /// <summary>
        /// Make error type eden data
        /// </summary>
        /// <param name="message">error message</param>
        /// <returns>EdenData : error type</returns>
        public static EdenData Error(string message)
        {
            return new EdenData(new EdenError(message));
        }
        
        /// <summary>
        /// Set JsonSerialize Option
        /// Default Option is {IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNul}
        /// </summary>
        /// <param name="options">JsonSerialize Option</param>
        public void SetSerializeOption(JsonSerializerOptions options)
        {
            _options = options;
        }
    }

    /// <summary>
    /// Struct : packet sturcture for EdenNetwork
    /// </summary>
    public struct EdenPacket
    {
        public string tag;
        public EdenData data;
    }

}
