using MelonLoader;
using System;
using System.Globalization;

namespace Si_HeavyTeleporter
{
    // Si_ModUI export: mirrors the /st chat commands only; chat stays fully usable without the UI.
    public partial class HeavyTeleporter
    {
        public static string ModUIData()
        {
            try
            {
                string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
                return ModUI.ModUIExport.New()
                    .Enabled(_enabled)
                    .Info("Heavy Teleporter " + (_enabled ? "ON" : "OFF") + " | tier >= " + TechTier + " | charges " + MaxCharges + " | recharge " + F(RechargeTime) + " s")
                    .Info("countdown " + F(Countdown) + " s | radius " + F(TeleportRadius) + " m | min enemy base distance " + F(MinEnemyBaseDistance) + " m")
                    .Action("/st", "Call vehicle", "Opens the menu of vehicles waiting at the Ultra Heavy Factory. Arguments: the menu number to confirm a pick, or for commanders a player name or x z.")
                    .Action("/st status", "Status", "Settings and the charges of each team.")
                    .OnOff("/st enabled", "Heavy Teleporter", "Master switch for the vehicle teleport.", admin: true, on: _enabled)
                    .Value("/st charges", "Charges per team", "Maximum charges per volley.", admin: true, current: MaxCharges.ToString(CultureInfo.InvariantCulture))
                    .Value("/st cd", "Recharge (s)", "Seconds until a charge is refilled.", admin: true, current: F(RechargeTime))
                    .Value("/st tier", "Tech tier", "Minimum tech tier a team needs.", admin: true, current: TechTier.ToString(CultureInfo.InvariantCulture))
                    .Value("/st countdown", "Countdown (s)", "Seconds of warning before the teleport executes.", admin: true, current: F(Countdown))
                    .Value("/st radius", "Pickup radius (m)", "Radius of the pickup zone around the factory waypoint.", admin: true, current: F(TeleportRadius))
                    .Value("/st mindist", "Min enemy base distance (m)", "Refuses a teleport closer than this to an enemy HQ or Nest.", admin: true, current: F(MinEnemyBaseDistance))
                    .Build();
            }
            catch (Exception exception) { MelonLogger.Warning("HeavyTeleporter ModUIData: " + exception.Message); return null; }
        }
    }
}
