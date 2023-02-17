﻿using System.Text.Json;
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
        private object[]? arrayData = null;
        [JsonIgnore]
        private Dictionary<string, object>? dictData = null;

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
        public EdenData(object data)
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
                arrayData = ParseData<object[]>(data);
            }
            else if (type == Type.DICTIONARY)
            {
                dictData = ParseData<Dictionary<string, object>>(data);
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
                return ParseData<T>(data);
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
                return TryParseData<T>(data, out result!);
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
            if (arrayData == null) throw new Exception("EdenData::Get(int idx) - data is null");
            if (type == Type.ARRAY)
            {
                if (idx < 0 || idx > arrayData.Length)
                {
                    throw new Exception("EdenData::Get(int idx) - out of index ");
                }
                return ParseData<T>(arrayData[idx])!;
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
            if (type == Type.ARRAY && arrayData != null)
            {
                if (idx < 0 || idx > arrayData.Length)
                    return TryParseData<T>(arrayData[idx], out result!);
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
            if (dictData == null) throw new Exception("EdenData::Get(string key) - data is null");
            if (type == Type.DICTIONARY)
            {
                if (dictData.TryGetValue(key, out var value) == false)
                    throw new Exception("EdenData::Get(string tag) - there is no tag in data dictionary");
                return ParseData<T>(value)!;
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
            if (type == Type.DICTIONARY && dictData != null)
            {
                if (dictData.TryGetValue(key, out var value))
                    return TryParseData<T>(value, out result!);
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
                return JsonSerializer.Deserialize<T>((JsonElement)data, new JsonSerializerOptions { IncludeFields = true });
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
                result = JsonSerializer.Deserialize<T>((JsonElement) data, new JsonSerializerOptions {IncludeFields = true});
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
