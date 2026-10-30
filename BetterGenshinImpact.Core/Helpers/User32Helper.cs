using System;

namespace BetterGenshinImpact.Helpers;

public class User32Helper
{
    public static GameKey ToVk(string key)
    {
        key = key.ToUpper();
        if (!key.StartsWith("VK_"))
        {
            key = $"VK_{key}";
        }

        return (GameKey)Enum.Parse(typeof(GameKey), key);
    }
}
