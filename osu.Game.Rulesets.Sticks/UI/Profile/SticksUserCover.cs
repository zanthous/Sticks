#nullable enable

using System;
using System.Reflection;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    /// <summary>
    /// The player's profile cover. Stable lazer draws it with UserCoverBackground, which Tachyon replaced with a general
    /// CoverBackground, so neither type compiles against both clients. The type is found at runtime instead; if a
    /// future client changes it again, the header simply has no cover.
    /// </summary>
    internal static class SticksUserCover
    {
        private static readonly Type? cover_type = typeof(APIUser).Assembly.GetType("osu.Game.Users.CoverBackground")
                                                   ?? typeof(APIUser).Assembly.GetType("osu.Game.Users.UserCoverBackground");

        public static Drawable Create(APIUser? user)
        {
            try
            {
                if (cover_type != null && Activator.CreateInstance(cover_type) is Drawable cover)
                {
                    // Tachyon takes the user as its IHasCover Model; stable lazer takes it as User.
                    PropertyInfo? model = cover_type.GetProperty("Model", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                          ?? cover_type.GetProperty("User", BindingFlags.Public | BindingFlags.Instance);
                    if (model?.CanWrite == true && (user == null || model.PropertyType.IsInstanceOfType(user)))
                    {
                        model.SetValue(cover, user);
                        cover.RelativeSizeAxes = Axes.Both;
                        return cover;
                    }
                }
            }
            catch (Exception e) when (e is TargetInvocationException or MissingMethodException or MemberAccessException or ArgumentException)
            {
                Logger.Log($"Sticks profile cover is unavailable on this client ({e.GetType().Name}).");
            }

            return Drawable.Empty();
        }
    }
}
