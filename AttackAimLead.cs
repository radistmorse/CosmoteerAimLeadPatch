using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts.Thrusters;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling;
using Halfling.Geometry;
using Halfling.Timing;
using HarmonyLib;

namespace CosmoteerAim
{
    [HarmonyPatch]
    public class AttackAimLead
    {
        /// <summary>
        /// This is the prefix for GetWorldRotation which will overwrite the worldFollowAngle.
        /// The worldFollowAngle is the angle at which we want to approach our target, but in
        /// GetWorldRotation it is used to calculate the ship heading. By modifying its value
        /// we could force the ship to lead the target.
        /// </summary>
        /// <param name="__instance">Injected object instance</param>
        /// <param name="worldFollowAngle">Injected method parameter</param>
        [HarmonyPatch(typeof(BaseFollowCommand), "GetWorldRotation")]
        [HarmonyPrefix]
        static public void GetWorldRotationPrefix(BaseFollowCommand __instance, ref Direction worldFollowAngle)
        {
            // do nothing if the command is wrong
            if (__instance is not AttackCommand || !__instance.HasTarget || __instance.RotationMode != FollowRotationMode.FollowAngleRelative) return;


            // find the fixed weapons aiming at the target and calculate the aiming points for them
            var aimLocs = __instance.Ship.Weapons.Where(weapon => weapon is FixedWeapon && weapon.ShipDirection == __instance.Rotation)
                                .Select(weapon => (valid: weapon.GetTargetingEmitter().TryGetAimLocation(__instance.Target, false, out var aimLoc)
                                                   && aimLoc != __instance.Target.DetWorldCenter, aimLoc))
                                .Where(x => x.valid)
                                .Select(x => x.aimLoc)
                                .ToList();

            if (aimLocs.Count == 0) return;

            // calculate the mean aiming point.
            // too bad if the ship has several weapons with
            // drastically different bullet speeds, none will hit
            var aimLoc = aimLocs.Aggregate((acc, cur) => acc + cur) / aimLocs.Count;

            // overwrite the angle to face the aiming point.
            // we calculate the angle based on the ship's actual location, not
            // on "planned" location. This is to make sure the rotation is correct
            // even if the ship drifted from it, although it may cause some quirks.
            worldFollowAngle = aimLoc.DirectionTo(__instance.Ship.DetWorldCenter);
            return;
        }

        /// <summary>
        /// This is the prefix for GetDesiredRotationalSRA which basically repeats
        /// the functionality of the default method, since it is not possible to
        /// change its behaviour otherwise. We want to change the resulting angular
        /// acceleration to keep the angular speed of the target.
        /// </summary>
        /// <param name="__instance">Injected object instance</param>
        /// <param name="__result">Injected result</param>
        /// <param name="interval">Injected methof parameter</param>
        /// <param name="desiredRot">Injected methof parameter</param>
        /// <param name="smartRotOffset">Injected methof parameter</param>
        /// <returns>True, if we still want to run the original method, false if not</returns>
        [HarmonyPatch(typeof(Command), "GetDesiredRotationalSRA")]
        [HarmonyPrefix]
        static public bool GetDesiredRotationalSRAPostfix(Command __instance, ref float __result, Time interval, Direction? desiredRot, ref Angle smartRotOffset)
        {
            // do nothing if the command is wrong
            if (__instance is not AttackCommand attackCommand || !desiredRot.HasValue) return true;

            // calculate the angular speed of the target
            var targetDirection = attackCommand.Target.DetWorldCenter - attackCommand.Ship.DetWorldCenter;
            var targetVelocity = attackCommand.Target.Physics.Body.LinearVelocity;
            var targetRotSpeed = targetDirection.Cross(targetVelocity) / targetDirection.LengthSquared;

            // the following is the default Command.GetDesiredRotationalSRA method, unless noted
            float curSRV = __instance.Ship.Physics.Body.AngularVelocity;
            Direction desRot = desiredRot.GetValueOrDefault();
            Angle rotOffset = __instance.Ship.DetRotation.PositiveAngleTo(desRot);
            Angle rotOffset2 = rotOffset - Angle.ThreeSixty;
            float decel;
            float rampUpTime;
            float rampDownTime;
            if (curSRV.Equals(0f))
            {
                decel = 0f;
                rampUpTime = 0f;
                rampDownTime = 0f;
            }
            else
            {
                ValueTuple<float, float, float> valueTuple = __instance.Ship.Thrusters
                                                                .CalculateMaximumAccelerationAndRampTimeCached(new Vector3(0f, 0f, -(float)Mathx.Sign(curSRV)),
                                                                                                               interval,
                                                                                                               __instance.Rules.ThrusterSRFStrafeFactors,
                                                                                                               ThrusterManager.ActivationRangeType.Deceleration);
                decel = valueTuple.Item1;
                rampUpTime = valueTuple.Item2;
                rampDownTime = valueTuple.Item3;
            }
            float sign = (float)Mathx.Sign(curSRV);
            decel *= -sign;
            float damping = __instance.Ship.Rules.MinAngularDamping * __instance.Ship.Nebulas.AngularDampingFactor;
            float curAccel = __instance.Ship.Thrusters.AngularAcceleration;
            // since we want to maintain the targetRotSpeed we only calculate the deceleration for the excess speed
            float decelerationSRV = curSRV - targetRotSpeed;
            // this is not exactly correct, since CalculateDecelerationDistance is not linear, but it is shit anyway so who cares.
            // I'm sorry, whoever wrote it, but it's true. There are simpler ways of solving quadratic equations than through numerical integration.
            float rotDecalOffset = __instance.CalculateDecelerationDistance(decelerationSRV * sign, decel * sign, rampUpTime, curAccel * sign, rampDownTime, damping, 0f) * sign;
            Angle smartRotOffset2 = rotOffset - rotDecalOffset;
            Angle smartRotOffset3 = rotOffset2 - rotDecalOffset;
            smartRotOffset = ((Mathx.Abs(smartRotOffset2) <= Mathx.Abs(smartRotOffset3)) ? smartRotOffset2 : smartRotOffset3);
            // we add the targetRotSpeed to the desiredSRV, as we want to maintain targetRotSpeed even when the smartRotOffset is zero
            float desiredSRV = targetRotSpeed + smartRotOffset * __instance.Ship.Rules.AngularVelocityCalculateFactor;

            // originally there were no parentheses, I have no idea what logic was behind it
            float desiredSRA = (desiredSRV - curSRV) * __instance.Ship.Rules.RetroAngularVelocityCalculateFactor;
            __result = Mathx.Clamp(desiredSRA,
                                   -__instance.Ship.Thrusters.CalculateMaximumAccelerationAndRampTimeCached(new Vector3(0f, 0f, -1f),
                                                                                                            interval,
                                                                                                            __instance.Rules.ThrusterSRFStrafeFactors,
                                                                                                            ThrusterManager.ActivationRangeType.Unclamped).Item1,
                                   __instance.Ship.Thrusters.CalculateMaximumAccelerationAndRampTimeCached(new Vector3(0f, 0f, 1f),
                                                                                                           interval,
                                                                                                           __instance.Rules.ThrusterSRFStrafeFactors,
                                                                                                           ThrusterManager.ActivationRangeType.Unclamped).Item1);
            return false;
        }
    }
}
