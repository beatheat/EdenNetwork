using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdenNetwork
{

    /// <summary>
    /// Struct : data type for respresent error
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
        private object[]? array_data = null;
        [JsonIgnore]
        private Dictionary<string, object>? dict_data = null;

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
                array_data = ParseData<object[]>(data);
            }
            else if (type == Type.DICTIONARY)
            {
                dict_data = ParseData<Dictionary<string, object>>(data);
            }
        }

        public string GetError()
        {
            if(type == Type.ERROR)
            {
                EdenError err = (EdenError)data;
                return err.text; 
            }
            return "";
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
        /// Get data by index from array object data
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="idx"> ndex desire</param>
        /// <returns>parsed data for type desired</returns>
        public T Get<T>(int idx)
        {
            if (array_data == null) throw new Exception("EdenData::Get(int idx) - data is null");
            if (type == Type.ARRAY)
            {
                if (idx < 0 || idx > array_data.Length)
                {
                    throw new Exception("EdenData::Get(int idx) - out of index ");
                }
#pragma warning disable CS8603
                return ParseData<T>(array_data[idx]);
#pragma warning restore
            }
            throw new Exception("EdenData::Get(int idx) - data is not array");
        }
        /// <summary>
        /// Get data by key from dictionary data
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="key">key desire</param>
        /// <returns>parsed data for type desired</returns>
        public T Get<T>(string key)
        {
            if (dict_data == null) throw new Exception("EdenData::Get(string key) - data is null");
            if (type == Type.DICTIONARY)
            {
                object? value;
                if (dict_data.TryGetValue(key, out value) == false)
                    throw new Exception("EdenData::Get(string tag) - there is no tag in data dictionary");
#pragma warning disable CS8603
                return ParseData<T>(value);
#pragma warning restore
            }
            throw new Exception("EdenData::Get(int idx) - data is not dictionary");
        }
        /// <summary>
        /// parse json data object to type desired
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="data">data object</param>
        /// <returns>parsed data for type desired</returns>
        public static T? ParseData<T>(object data)
        {
            return JsonSerializer.Deserialize<T>((JsonElement)data, new JsonSerializerOptions { IncludeFields = true });
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
