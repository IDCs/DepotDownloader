using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Common
{
  public static class Exceptions
  {
    public class OperationFinishedException : Exception
    {
      public OperationFinishedException()
        : base("Operation completed successfully") { }
    }
  }
  public static class Util
  {
    public static object? ValueNormalize(object input)
    {
      if (input == null || input == "null")
      {
        return null;
      }

      if (input.GetType() == typeof(JValue))
      {
        input = ((JValue)input).Value;
      }

      if (input.GetType() == typeof(JArray))
      {
        dynamic binder = new ArrayBinder((JArray)input);
        return (object[])binder;
      }
      else if (input.GetType() == typeof(JObject))
      {
        IDictionary<string, object> res = ((JObject)input).ToObject<IDictionary<string, object>>();
        if (res.ContainsKey("type") && ((string)res["type"] == "Buffer"))
        {
          return Convert.FromBase64String((string)res["data"]);
        }
        else
        {
          return new DictWrap(res);
        }
      }
      else if (input.GetType() == typeof(long))
      {
        // rebox System.Int64 to System.Int32 because that's what the installer is
        // expecting and I found no good way to do the cast implicitly as necessary.
        // my C#-fu is just to weak
        return (int)(long)input;
      }
      else if (input.GetType() == typeof(ulong))
      {
        return Convert.ToUInt64(input);
      }
      else if (input.GetType() == typeof(bool))
      {
        return (bool)input;
      }
      else
      {
        return input;
      }
    }
    public static object ValueNormalize<T>(object input, T defaultValue = default(T))
    {
      if (input == null)
      {
        return defaultValue;
      }

      if (input.GetType() == typeof(JValue))
      {
        input = ((JValue)input).Value;
      }

      if (input.GetType() == typeof(JArray))
      {
        dynamic binder = new ArrayBinder((JArray)input);
        return (object[])binder;
      }
      else if (input.GetType() == typeof(JObject))
      {
        IDictionary<string, object> res = ((JObject)input).ToObject<IDictionary<string, object>>();
        if (res.ContainsKey("type") && ((string)res["type"] == "Buffer"))
        {
          return Convert.FromBase64String((string)res["data"]);
        }
        else
        {
          return new DictWrap(res);
        }
      }
      else if (input.GetType() == typeof(long))
      {
        // rebox System.Int64 to System.Int32 because that's what the installer is
        // expecting and I found no good way to do the cast implicitly as necessary.
        // my C#-fu is just to weak
        return (int)(long)input;
      }
      else if (typeof(T) == typeof(ulong))
      {
        return Convert.ToUInt64(input);
      }
      else if (input.GetType() == typeof(bool))
      {
        return (bool)input;
      }
      else
      {
        return input == null ? defaultValue : input;
      }
    }

    /// <summary>
    /// run a task but throw a timeout exception if it doesn't complete in time
    /// </summary>
    /// <param name="task"></param>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public static async Task<object> Timeout(Task<object> task, int timeout)
    {
      var res = await Task.WhenAny(task, Task.Delay(timeout));
      if (res == task)
      {
        return await task;
      }
      else
      {
        throw new TimeoutException("task timeout");
      }
    }

    /**
     * dynamic object allowing access to a dictionary through regular object syntax (obj.foobar instead of dict["foobar"]).
     * currently read only
     */
    public class DictWrap : DynamicObject
    {

      private IDictionary<string, object> mWrappee;
      public DictWrap(IDictionary<string, object> dict)
      {
        mWrappee = dict;
      }

      public override bool TryGetMember(GetMemberBinder binder, out object result)
      {
        if (mWrappee.ContainsKey(binder.Name))
        {
          result = ValueNormalize(mWrappee[binder.Name]);
          return true;
        }
        else
        {
          result = null;
          return false;
        }
      }
    }

    /**
     * Allow a dynamic list type (JArray) to be implicitly converted to an array or any enumerable
     * as needed
     **/
    private class ArrayBinder : DynamicObject
    {
      private readonly JArray mArray;

      public ArrayBinder(JArray arr)
      {
        mArray = arr;
      }

      public override bool TryConvert(ConvertBinder binder, out object result)
      {
        IEnumerable<object> arr = mArray.Select(token => ValueNormalize(token));

        // some kind of array
        if (binder.Type.IsArray)
        {
          result = arr.ToArray();
        }
        else if (binder.Type.GetMethod("GetEnumerator") != null)
        {
          result = arr;
        }
        else
        {
          result = null;
        }
        return true;
      }
    }
  }
}

