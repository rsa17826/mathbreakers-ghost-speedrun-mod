using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// MiniJSON - a minimal JSON parser/serializer with zero dependencies.
/// Represents JSON as plain Dictionary&lt;string, object&gt; / List&lt;object&gt;
/// / string / double / bool / null - nothing that requires the .NET DLR
/// (System.Dynamic), unlike Newtonsoft.Json.Linq's JObject/JArray, which
/// implement IDynamicMetaObjectProvider and fail to load entirely on
/// Unity 4.2's very old embedded Mono runtime.
///
/// Public domain. Originally by Calvin Rien, based on the JSON parser by
/// Patrick van Bergen (widely used across the Unity community for exactly
/// this old-runtime-compatibility reason).
/// </summary>
public static class Json
{
  public static object Deserialize(string json)
  {
    if (json == null)
      return null;
    return Parser.Parse(json);
  }

  public static string Serialize(object obj)
  {
    return Serializer.Serialize(obj);
  }

  private sealed class Parser : IDisposable
  {
    private const string WhiteSpace = " \t\n\r";
    private const string WordBreak = " \t\n\r{}[],:\"";

    private enum Token
    {
      None,
      CurlyOpen,
      CurlyClose,
      SquareOpen,
      SquareClose,
      Colon,
      Comma,
      String,
      Number,
      True,
      False,
      Null,
    }

    private System.IO.StringReader json;

    private Parser(string jsonString)
    {
      json = new System.IO.StringReader(jsonString);
    }

    public static object Parse(string jsonString)
    {
      using (var instance = new Parser(jsonString))
      {
        return instance.ParseValue();
      }
    }

    public void Dispose()
    {
      json.Dispose();
      json = null;
    }

    private Dictionary<string, object> ParseObject()
    {
      var table = new Dictionary<string, object>();
      json.Read(); // {

      while (true)
      {
        switch (NextToken)
        {
          case Token.None:
            return null;
          case Token.Comma:
            continue;
          case Token.CurlyClose:
            return table;
          default:
            string name = ParseString();
            if (name == null)
              return null;

            if (NextToken != Token.Colon)
              return null;
            json.Read(); // :

            table[name] = ParseValue();
            break;
        }
      }
    }

    private List<object> ParseArray()
    {
      var array = new List<object>();
      json.Read(); // [

      var parsing = true;
      while (parsing)
      {
        Token nextToken = NextToken;

        switch (nextToken)
        {
          case Token.None:
            return null;
          case Token.Comma:
            continue;
          case Token.SquareClose:
            parsing = false;
            break;
          default:
            array.Add(ParseByToken(nextToken));
            break;
        }
      }

      return array;
    }

    private object ParseValue()
    {
      Token nextToken = NextToken;
      return ParseByToken(nextToken);
    }

    private object ParseByToken(Token token)
    {
      switch (token)
      {
        case Token.String:
          return ParseString();
        case Token.Number:
          return ParseNumber();
        case Token.CurlyOpen:
          return ParseObject();
        case Token.SquareOpen:
          return ParseArray();
        case Token.True:
          return true;
        case Token.False:
          return false;
        case Token.Null:
          return null;
        default:
          return null;
      }
    }

    private string ParseString()
    {
      var s = new StringBuilder();
      json.Read(); // "

      bool parsing = true;
      while (parsing)
      {
        if (json.Peek() == -1)
        {
          parsing = false;
          break;
        }

        char c = NextChar;
        switch (c)
        {
          case '"':
            parsing = false;
            break;
          case '\\':
            if (json.Peek() == -1)
            {
              parsing = false;
              break;
            }

            c = NextChar;
            switch (c)
            {
              case '"':
              case '\\':
              case '/':
                s.Append(c);
                break;
              case 'b':
                s.Append('\b');
                break;
              case 'f':
                s.Append('\f');
                break;
              case 'n':
                s.Append('\n');
                break;
              case 'r':
                s.Append('\r');
                break;
              case 't':
                s.Append('\t');
                break;
              case 'u':
                var hex = new char[4];
                for (int i = 0; i < 4; i++)
                  hex[i] = NextChar;
                s.Append((char)Convert.ToInt32(new string(hex), 16));
                break;
            }
            break;
          default:
            s.Append(c);
            break;
        }
      }

      return s.ToString();
    }

    private object ParseNumber()
    {
      string number = NextWord;

      if (number.IndexOf('.') == -1 && number.IndexOf('e') == -1 && number.IndexOf('E') == -1)
      {
        long longVal;
        if (long.TryParse(number, out longVal))
          return longVal;
      }

      double doubleVal;
      double.TryParse(
        number,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture,
        out doubleVal
      );
      return doubleVal;
    }

    private void EatWhitespace()
    {
      while (WhiteSpace.IndexOf(PeekChar) != -1)
      {
        json.Read();
        if (json.Peek() == -1)
          break;
      }
    }

    private char PeekChar
    {
      get { return Convert.ToChar(json.Peek()); }
    }

    private char NextChar
    {
      get { return Convert.ToChar(json.Read()); }
    }

    private string NextWord
    {
      get
      {
        var word = new StringBuilder();
        while (WordBreak.IndexOf(PeekChar) == -1)
        {
          word.Append(NextChar);
          if (json.Peek() == -1)
            break;
        }
        return word.ToString();
      }
    }

    private Token NextToken
    {
      get
      {
        EatWhitespace();
        if (json.Peek() == -1)
          return Token.None;

        switch (PeekChar)
        {
          case '{':
            return Token.CurlyOpen;
          case '}':
            json.Read();
            return Token.CurlyClose;
          case '[':
            return Token.SquareOpen;
          case ']':
            json.Read();
            return Token.SquareClose;
          case ',':
            json.Read();
            return Token.Comma;
          case '"':
            return Token.String;
          case ':':
            return Token.Colon;
          case '0':
          case '1':
          case '2':
          case '3':
          case '4':
          case '5':
          case '6':
          case '7':
          case '8':
          case '9':
          case '-':
            return Token.Number;
        }

        switch (NextWord)
        {
          case "false":
            return Token.False;
          case "true":
            return Token.True;
          case "null":
            return Token.Null;
        }

        return Token.None;
      }
    }
  }

  private sealed class Serializer
  {
    private readonly StringBuilder builder;

    private Serializer()
    {
      builder = new StringBuilder();
    }

    public static string Serialize(object obj)
    {
      var instance = new Serializer();
      instance.SerializeValue(obj);
      return instance.builder.ToString();
    }

    private void SerializeValue(object value)
    {
      if (value == null)
      {
        builder.Append("null");
      }
      else if (value is string)
      {
        SerializeString((string)value);
      }
      else if (value is bool)
      {
        builder.Append((bool)value ? "true" : "false");
      }
      else if (value is IDictionary)
      {
        SerializeObject((IDictionary)value);
      }
      else if (value is IList)
      {
        SerializeArray((IList)value);
      }
      else if (value is char)
      {
        SerializeString(value.ToString());
      }
      else
      {
        SerializeOther(value);
      }
    }

    private void SerializeObject(IDictionary obj)
    {
      bool first = true;
      builder.Append('{');

      foreach (var key in obj.Keys)
      {
        if (!first)
          builder.Append(',');
        SerializeString(key.ToString());
        builder.Append(':');
        SerializeValue(obj[key]);
        first = false;
      }

      builder.Append('}');
    }

    private void SerializeArray(IList array)
    {
      builder.Append('[');
      bool first = true;
      foreach (object obj in array)
      {
        if (!first)
          builder.Append(',');
        SerializeValue(obj);
        first = false;
      }
      builder.Append(']');
    }

    private void SerializeString(string str)
    {
      builder.Append('"');
      foreach (var c in str)
      {
        switch (c)
        {
          case '"':
            builder.Append("\\\"");
            break;
          case '\\':
            builder.Append("\\\\");
            break;
          case '\b':
            builder.Append("\\b");
            break;
          case '\f':
            builder.Append("\\f");
            break;
          case '\n':
            builder.Append("\\n");
            break;
          case '\r':
            builder.Append("\\r");
            break;
          case '\t':
            builder.Append("\\t");
            break;
          default:
            int codepoint = Convert.ToInt32(c);
            if (codepoint >= 32 && codepoint <= 126)
            {
              builder.Append(c);
            }
            else
            {
              builder.Append("\\u").Append(codepoint.ToString("x4"));
            }
            break;
        }
      }
      builder.Append('"');
    }

    private void SerializeOther(object value)
    {
      if (
        value is float
        || value is int
        || value is uint
        || value is long
        || value is double
        || value is sbyte
        || value is byte
        || value is short
        || value is ushort
        || value is ulong
        || value is decimal
      )
      {
        builder.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
      }
      else
      {
        SerializeString(value.ToString());
      }
    }
  }
}
