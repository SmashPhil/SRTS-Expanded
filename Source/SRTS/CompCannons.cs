﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;

using Verse.Sound;


namespace SRTS
{
    public class CompCannons : ThingComp
    {
        private float range;
        private List<Tuple<Stack<int>, CannonHandler, int>> broadsideFire = new List<Tuple<Stack<int>, CannonHandler, int>>();

        // PARAMS => (# Shots Fired, CannonHandler, {tickCount, indexing}
        public List<Tuple<int, CannonHandler, Tuple<int,int>>> multiFireCannon = new List<Tuple<int, CannonHandler, Tuple<int,int>>>();

        private List<CannonHandler> cannons = new List<CannonHandler>();
        private const float cellOffsetIntVec3ToVector3 = 0.5f;
        public CompProperties_Cannons Props => (CompProperties_Cannons)props;
        public Pawn Pawn => parent as Pawn;
        public CompVehicle CompVehicle => this.Pawn.GetComp<CompVehicle>();

        public bool WeaponStatusOnline => !this.Pawn.Downed && !this.Pawn.Dead && this.Pawn.Drafted;

        public float MinRange => Cannons.Max(x => x.cannonDef.minRange);
        public float MaxRangeGrouped
        {
            get
            {
                IEnumerable<CannonHandler> cannonRange = Cannons.Where(x => x.cannonDef.maxRange <= GenRadial.MaxRadialPatternRadius);
                if(!cannonRange.AnyNullified())
                {
                    return (float)Math.Floor(GenRadial.MaxRadialPatternRadius);
                }
                return cannonRange.Min(x => x.cannonDef.maxRange);
            }
        }

        public List<CannonHandler> Cannons
        {
            get
            {
                if (cannons is null)
                {
                    cannons = new List<CannonHandler>();
                }
                return cannons;
            }
        }

        public void AddCannons(List<CannonHandler> cannonList)
        {
            if (cannonList is null)
                return;
            foreach(CannonHandler cannon in cannonList)
            {
                var cannonPermanent = new CannonHandler(this.Pawn, cannon);
                cannonPermanent.SetTarget(LocalTargetInfo.Invalid);
                cannonPermanent.ResetCannonAngle();
                if(Cannons.AnyNullified(x => x.baseCannonRenderLocation == cannonPermanent.baseCannonRenderLocation))
                {
                    Cannons.FindAll(x => x.baseCannonRenderLocation == cannonPermanent.baseCannonRenderLocation).ForEach(y => y.TryRemoveShell());
                    Cannons.RemoveAll(x => x.baseCannonRenderLocation == cannonPermanent.baseCannonRenderLocation);
                }
                Cannons.Add(cannonPermanent);
            }
        }

        public float Range
        {
            get
            {
                if (range <= 0) range = MaxRangeGrouped;
                return this.range;
            }
            set
            {
                range = SPMultiCell.Clamp(value, MinRange, MaxRangeGrouped);
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (Cannons?.Where(x => x.cannonDef.weaponType == WeaponType.Static).Count() > 0 && this.Pawn.Drafted)
            {
                if(MinRange > 0)
                {
                    GenDraw.DrawRadiusRing(this.Pawn.DrawPosTransformed(CompVehicle.Props.hitboxOffsetX, CompVehicle.Props.hitboxOffsetZ, CompVehicle.Angle).ToIntVec3(), this.MinRange, Color.red);
                }
                GenDraw.DrawRadiusRing(this.Pawn.DrawPosTransformed(CompVehicle.Props.hitboxOffsetX, CompVehicle.Props.hitboxOffsetZ, CompVehicle.Angle).ToIntVec3(), this.Range);
            }
        }

        public override void PostDraw()
        {
            foreach (CannonHandler cannon in Cannons.OrderBy(x => x.drawLayer))
            {
                cannon.Draw();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (this.Pawn.Drafted)
            {
                if (Cannons.Count > 0)
                {
                    if(Cannons.AnyNullified(x => x.cannonDef.weaponType == WeaponType.Rotatable))
                    {
                        int i = 0;
                        foreach(CannonHandler cannon in Cannons.Where(x => x.cannonDef.weaponLocation == WeaponLocation.Turret))
                        {
                            if(cannon.manualTargeting)
                            {
                                Command_TargeterCooldownAction turretCannons = new Command_TargeterCooldownAction();
                                turretCannons.cannon = cannon;
                                turretCannons.comp = this;
                                turretCannons.defaultLabel = !string.IsNullOrEmpty(cannon.gizmoLabel) ? cannon.gizmoLabel : cannon.cannonDef.LabelCap + i;
                                turretCannons.icon = cannon.GizmoIcon;
                                if(!string.IsNullOrEmpty(cannon.cannonDef.gizmoDescription))
                                    turretCannons.defaultDesc = cannon.cannonDef.gizmoDescription;
                                turretCannons.targetingParams = new TargetingParameters
                                {
                                    //Buildings, Things, Animals, Humans, and Mechs default to targetable
                                    canTargetLocations = true
                                };
                                i++;
                                foreach(VehicleHandler relatedHandler in CompVehicle.GetAllHandlersMatch(HandlingTypeFlags.Cannon, cannon.key))
                                {
                                    if(!VehicleMod.mod.settings.debugDraftAnyShip && relatedHandler?.handlers.Count < relatedHandler?.role.slotsToOperate)
                                    {
                                        turretCannons.Disable("NotEnoughCannonCrew".Translate(this.Pawn.LabelShort, relatedHandler.role.label));
                                        break;
                                    }
                                }
                                yield return turretCannons;
                            }
                        }
                    }
                    if (Cannons.AnyNullified(x => x.cannonDef.weaponType == WeaponType.Static))
                    {
                        if (Cannons.AnyNullified(x => x.cannonDef.weaponLocation == WeaponLocation.Port))
                        {
                            foreach(CannonHandler cannon in Cannons.Where(x => x.cannonDef.weaponLocation == WeaponLocation.Port))
                            {
                                Command_CooldownAction portSideCannons = new Command_CooldownAction();
                                portSideCannons.cannon = cannon;
                                portSideCannons.comp = this;
                                portSideCannons.defaultLabel = "CannonLabel".Translate(cannon.cannonDef.label);
                                if(!string.IsNullOrEmpty(cannon.cannonDef.gizmoDescription))
                                    portSideCannons.defaultDesc = cannon.cannonDef.gizmoDescription;
                                portSideCannons.icon = cannon.GizmoIcon;
                                if(!string.IsNullOrEmpty(cannon.cannonDef.gizmoDescription))
                                    portSideCannons.defaultDesc = cannon.cannonDef.gizmoDescription;
                                portSideCannons.action = delegate ()
                                {
                                    SPTuple<Stack<int>, CannonHandler, int> tmpCannonItem = new SPTuple<Stack<int>, CannonHandler, int>(new Stack<int>(), cannon, 0);
                                    List<int> cannonOrder = Enumerable.Range(0, cannon.cannonDef.numberCannons).ToList();
                                    if(VehicleMod.mod.settings.shuffledCannonFire)
                                        cannonOrder.Shuffle();
                                    foreach (int i in cannonOrder)
                                    {
                                        tmpCannonItem.First.Push(i);
                                    }
                                    this.broadsideFire.Add(tmpCannonItem);
                                };
                                foreach (VehicleHandler relatedHandler in CompVehicle.GetAllHandlersMatch(HandlingTypeFlags.Cannon, cannon.key))
                                {
                                    if(!VehicleMod.mod.settings.debugDraftAnyShip && relatedHandler?.handlers.Count < relatedHandler?.role.slotsToOperate)
                                    {
                                        portSideCannons.Disable("NotEnoughCannonCrew".Translate(this.Pawn.LabelShort, relatedHandler.role.label));
                                    }
                                }
                                yield return portSideCannons;
                            }
                        }
                        if (Cannons.AnyNullified(x => x.cannonDef.weaponLocation == WeaponLocation.Starboard))
                        {
                            foreach(CannonHandler cannon in Cannons.Where(x => x.cannonDef.weaponLocation == WeaponLocation.Starboard))
                            {
                                Command_CooldownAction starboardSideCannons = new Command_CooldownAction();
                                starboardSideCannons.cannon = cannon;
                                starboardSideCannons.comp = this;
                                starboardSideCannons.defaultLabel = "CannonLabel".Translate(cannon.cannonDef.label);
                                if(!string.IsNullOrEmpty(cannon.cannonDef.gizmoDescription))
                                    starboardSideCannons.defaultDesc = cannon.cannonDef.gizmoDescription;
                                starboardSideCannons.icon = cannon.GizmoIcon;
                                starboardSideCannons.action = delegate ()
                                {
                                    SPTuple<Stack<int>, CannonHandler, int> tmpCannonItem = new SPTuple<Stack<int>, CannonHandler, int>(new Stack<int>(), cannon, 0);
                                    List<int> cannonOrder = Enumerable.Range(0, cannon.cannonDef.numberCannons).ToList();
                                    if (VehicleMod.mod.settings.shuffledCannonFire)
                                        cannonOrder.Shuffle();
                                    foreach (int i in cannonOrder)
                                    {
                                        tmpCannonItem.First.Push(i);
                                    }
                                    this.broadsideFire.Add(tmpCannonItem);
                                };
                                foreach (VehicleHandler relatedHandler in CompVehicle.GetAllHandlersMatch(HandlingTypeFlags.Cannon, cannon.key))
                                {
                                    if(!VehicleMod.mod.settings.debugDraftAnyShip && relatedHandler?.handlers.Count < relatedHandler?.role.slotsToOperate)
                                    {
                                        starboardSideCannons.Disable("NotEnoughCannonCrew".Translate(this.Pawn.LabelShort, relatedHandler.role.label));
                                    }
                                }
                                yield return starboardSideCannons;
                            }
                        }
                        if (Cannons.AnyNullified(x => x.cannonDef.weaponLocation == WeaponLocation.Bow))
                        {
                            foreach(CannonHandler cannon in Cannons.Where(x => x.cannonDef.weaponLocation == WeaponLocation.Bow))
                            {
                                Command_CooldownAction bowSideCannons = new Command_CooldownAction();
                                bowSideCannons.cannon = cannon;
                                bowSideCannons.comp = this;
                                bowSideCannons.defaultLabel = "CannonLabel".Translate(cannon.cannonDef.label);
                                if(!string.IsNullOrEmpty(cannon.cannonDef.gizmoDescription))
                                    bowSideCannons.defaultDesc = cannon.cannonDef.gizmoDescription;
                                bowSideCannons.icon = cannon.GizmoIcon;
                                bowSideCannons.action = delegate ()
                                {
                                    SPTuple<Stack<int>, CannonHandler, int> tmpCannonItem = new SPTuple<Stack<int>, CannonHandler, int>(new Stack<int>(), cannon, 0);
                                    List<int> cannonOrder = Enumerable.Range(0, cannon.cannonDef.numberCannons).ToList();
                                    if (VehicleMod.mod.settings.shuffledCannonFire)
                                        cannonOrder.Shuffle();
                                    foreach (int i in cannonOrder)
                                    {
                                        tmpCannonItem.First.Push(i);
                                    }
                                    this.broadsideFire.Add(tmpCannonItem);
                                };
                                foreach (VehicleHandler relatedHandler in CompVehicle.GetAllHandlersMatch(HandlingTypeFlags.Cannon, cannon.key))
                                {
                                    if(!VehicleMod.mod.settings.debugDraftAnyShip && relatedHandler?.handlers.Count < relatedHandler?.role.slotsToOperate)
                                    {
                                        bowSideCannons.Disable("NotEnoughCannonCrew".Translate(this.Pawn.LabelShort, relatedHandler.role.label));
                                    }
                                }
                                yield return bowSideCannons;
                            }
                        }
                        if (Cannons.AnyNullified(x => x.cannonDef.weaponLocation == WeaponLocation.Stern))
                        {
                            foreach(CannonHandler cannon in Cannons.Where(x => x.cannonDef.weaponLocation == WeaponLocation.Starboard))
                            {
                                Command_CooldownAction sternSideCannons = new Command_CooldownAction();
                                sternSideCannons.cannon = cannon;
                                sternSideCannons.comp = this;
                                sternSideCannons.defaultLabel = "CannonLabel".Translate(cannon.cannonDef.label);
                                if(!string.IsNullOrEmpty(cannon.cannonDef.gizmoDescription))
                                    sternSideCannons.defaultDesc = cannon.cannonDef.gizmoDescription;
                                sternSideCannons.icon = cannon.GizmoIcon;
                                sternSideCannons.action = delegate ()
                                {
                                    SPTuple<Stack<int>, CannonHandler, int> tmpCannonItem = new SPTuple<Stack<int>, CannonHandler, int>(new Stack<int>(), cannon, 0);
                                    List<int> cannonOrder = Enumerable.Range(0, cannon.cannonDef.numberCannons).ToList();
                                    if (VehicleMod.mod.settings.shuffledCannonFire)
                                        cannonOrder.Shuffle();
                                    foreach (int i in cannonOrder)
                                    {
                                        tmpCannonItem.First.Push(i);
                                    }
                                    this.broadsideFire.Add(tmpCannonItem);
                                };
                                foreach (VehicleHandler relatedHandler in CompVehicle.GetAllHandlersMatch(HandlingTypeFlags.Cannon, cannon.key))
                                {
                                    if(!VehicleMod.mod.settings.debugDraftAnyShip && relatedHandler?.handlers.Count < relatedHandler?.role.slotsToOperate)
                                    {
                                        sternSideCannons.Disable("NotEnoughCannonCrew".Translate(this.Pawn.LabelShort, relatedHandler.role.label));
                                    }
                                }
                                yield return sternSideCannons;
                            }
                        }

                        Command_SetRange range = new Command_SetRange();
                        range.defaultLabel = "SetRange".Translate();
                        range.icon = TexCommand.Attack;
                        range.activeCannons = Cannons.FindAll(x => x.cannonDef.weaponType == WeaponType.Static);
                        range.cannonComp = this;
                        yield return range;
                    }
                }
            }
        }

        private void ResolveCannons()
        {
            if (!this.Pawn.Drafted && broadsideFire.Count > 0)
            {
                broadsideFire.Clear();
            }
            
            if (broadsideFire?.Count > 0)
            {
                for (int i = 0; i < broadsideFire.Count; i++)
                {
                    SPTuple<Stack<int>, CannonHandler> side = broadsideFire[i];
                    int tick = broadsideFire[i].Third;
                    if(broadsideFire[i].Third % side.Second.TicksPerShot == 0)
                    {
                        FireCannonBroadside(side.Second, side.First.Pop());
                        
                    }
                    tick++;
                    broadsideFire[i].Third = tick;
                    if (!side.First.AnyNullified())
                    {
                        broadsideFire.RemoveAt(i);
                    }
                }
            }

            if(multiFireCannon?.Count > 0)
            {
                for(int i = 0; i < multiFireCannon.Count; i++)
                {
                    SPTuple2<int, int> PairedData = multiFireCannon[i].Third;
                    if (PairedData.First <= 0)
                    {
                        FireTurretCannon(multiFireCannon[i].Second, ref PairedData);
                        PairedData.Second++;
                        multiFireCannon[i].First--;
                        PairedData.First = multiFireCannon[i].Second.TicksPerShot;
                        if (multiFireCannon[i].First == 0)
                        {
                            if(multiFireCannon[i].Second.targetPersists)
                                multiFireCannon[i].Second.SetTargetConditionalOnThing(LocalTargetInfo.Invalid);
                            else
                                multiFireCannon[i].Second.SetTarget(LocalTargetInfo.Invalid);
                            multiFireCannon.RemoveAt(i);
                            continue;
                        }
                    }
                    else
                    {
                        PairedData.First--;
                    }
                    multiFireCannon[i].Third = PairedData;
                }
            }
        }

        public void FireTurretCannon(CannonHandler cannon, ref SPTuple2<int,int> data)
        {
            if (cannon is null)
                return;
            TryFindShootLineFromTo(cannon.TurretLocation.ToIntVec3(), cannon.cannonTarget, out ShootLine shootLine);

            IntVec3 c = cannon.cannonTarget.Cell + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(cannon.cannonDef.spreadRadius * (Range / cannon.cannonDef.maxRange)))];
            if (data.Second >= cannon.cannonDef.projectileShifting.Count)
                data.Second = 0;
            float horizontalOffset = cannon.cannonDef.projectileShifting.AnyNullified() ? cannon.cannonDef.projectileShifting[data.Second] : 0;
            Vector3 launchCell = cannon.TurretLocation + new Vector3(horizontalOffset, 1f, cannon.cannonDef.projectileOffset).RotatedBy(cannon.TurretRotation);

            ThingDef projectile;
            if(!cannon.cannonDef.ammoAllowed.NullOrEmpty())
            {
                projectile = cannon.loadedAmmo?.projectileWhenLoaded;
            }
            else
            {
                projectile = cannon.cannonDef.projectile;
            }
            try
            {
                Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, Pawn.Position, Pawn.Map, WipeMode.Vanish);
                if(!cannon.cannonDef.ammoAllowed.NullOrEmpty())
                {
                    cannon.ConsumeShellChambered();
                }
                if (cannon.cannonDef.cannonSound is null) SoundDefOf_Ships.Explosion_PirateCannon.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false));
                else { cannon.cannonDef.cannonSound.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false)); }

                if(cannon.cannonDef.moteFlash != null)
                {
                    MoteMaker.MakeStaticMote(launchCell, Pawn.Map, cannon.cannonDef.moteFlash, 2);
                }
                if (cannon.cannonDef.moteCannon != null)
                {
                    MoteThrown mote = (MoteThrown)ThingMaker.MakeThing(cannon.cannonDef.moteCannon, null);
                    mote.exactPosition = launchCell;
                    mote.Scale = 1f;
                    mote.rotationRate = 15f;
                    mote.SetVelocity(cannon.TurretRotation, cannon.cannonDef.moteSpeedThrown);
                    HelperMethods.ThrowMoteEnhanced(launchCell, Pawn.Map, mote);
                }
                projectile2.Launch(Pawn, launchCell, c, cannon.cannonTarget, cannon.cannonDef.hitFlags, parent);
                if(cannon.cannonDef.graphicData.graphicClass == typeof(Graphic_Animate))
                {
                    cannon.StartAnimation(2, 1, AnimationWrapperType.Reset);
                }
            }
            catch(Exception ex)
            {
                Log.Error($"Exception when firing Cannon: {cannon.cannonDef.LabelCap} on Pawn: {Pawn.LabelCap}. Exception: {ex.Message}");
            }
        }

        public void FireCannonBroadside(CannonHandler cannon, int i)
        {
            if (cannon is null) return;
            float initialOffset;
            float offset;
            bool mirrored = false;
            if (this.Pawn.Rotation == Rot4.South || Pawn.Rotation == Rot4.West)
                mirrored = true;
            if(cannon.cannonDef.splitCannonGroups)
            {
                int group = cannon.CannonGroup(i);
                float groupOffset = cannon.cannonDef.centerPoints[group];
                initialOffset = ((cannon.cannonDef.spacing * (cannon.cannonDef.cannonsPerPoint[group] - 1)) / 2f) + groupOffset; // s(n-1) / 2
                offset = (cannon.cannonDef.spacing * i - initialOffset) * (mirrored ? -1 : 1); //s*i - x
            }
            else
            {
                initialOffset = ((cannon.cannonDef.spacing * (cannon.cannonDef.numberCannons - 1)) / 2f) + cannon.cannonDef.offset; // s(n-1) / 2
                offset = (cannon.cannonDef.spacing * i - initialOffset) * (mirrored ? -1 : 1); //s*i - x
            }

            float projectileOffset = (this.Pawn.def.size.x / 2f) + cannon.cannonDef.projectileOffset; // (s/2)
            SPTuple2<float, float> angleOffset = AngleRotationProjectileOffset(offset, projectileOffset);
            ThingDef projectile = cannon.cannonDef.projectile;
            IntVec3 targetCell = IntVec3.Invalid;
            Vector3 launchCell = this.Pawn.DrawPos;
            switch (cannon.cannonDef.weaponLocation)
            {
                case WeaponLocation.Port:
                    if (this.CompVehicle.Angle == 0)
                    {
                        if (this.Pawn.Rotation == Rot4.North)
                        {
                            launchCell.x -= projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.East)
                        {
                            launchCell.x += offset;
                            launchCell.z += projectileOffset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.South)
                        {
                            launchCell.x += projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.West)
                        {
                            launchCell.x += offset;
                            launchCell.z -= projectileOffset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z -= (int)this.Range;
                        }
                    }
                    else
                    {
                        if (this.Pawn.Rotation == Rot4.East && this.CompVehicle.Angle == -45)
                        {
                            launchCell.x += angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.East && this.CompVehicle.Angle == 45)
                        {
                            launchCell.x += angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompVehicle.Angle == -45)
                        {
                            launchCell.x -= angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompVehicle.Angle == 45)
                        {
                            launchCell.x -= angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                    }
                    break;
                case WeaponLocation.Starboard:
                    if (this.CompVehicle.Angle == 0)
                    {
                        if (this.Pawn.Rotation == Rot4.North)
                        {
                            launchCell.x += projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.East)
                        {
                            launchCell.z -= projectileOffset;
                            launchCell.x += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z -= (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.South)
                        {
                            launchCell.x -= projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.West)
                        {
                            launchCell.z += projectileOffset;
                            launchCell.x += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z += (int)this.Range;
                        }
                    }
                    else
                    {
                        if (this.Pawn.Rotation == Rot4.East && this.CompVehicle.Angle == -45)
                        {
                            launchCell.x += angleOffset.Second;
                            launchCell.z += angleOffset.First;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.East && this.CompVehicle.Angle == 45)
                        {
                            launchCell.x -= angleOffset.Second;
                            launchCell.z -= angleOffset.First;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompVehicle.Angle == -45)
                        {
                            launchCell.x += angleOffset.Second;
                            launchCell.z -= angleOffset.First;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompVehicle.Angle == 45)
                        {
                            launchCell.x -= angleOffset.Second;
                            launchCell.z += angleOffset.First;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                    }
                    break;
                case WeaponLocation.Bow:
                    if (this.CompVehicle.Angle == 0)
                    {
                        if (this.Pawn.Rotation == Rot4.North)
                        {
                            launchCell.x -= projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.East)
                        {
                            launchCell.x += offset;
                            launchCell.z += projectileOffset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.South)
                        {
                            launchCell.x += projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.West)
                        {
                            launchCell.x += offset;
                            launchCell.z -= projectileOffset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z -= (int)this.Range;
                        }
                    }
                    else
                    {
                        if (this.Pawn.Rotation == Rot4.East && this.CompVehicle.Angle == -45)
                        {
                            launchCell.x += angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.East && this.CompVehicle.Angle == 45)
                        {
                            launchCell.x += angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompVehicle.Angle == -45)
                        {
                            launchCell.x -= angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompVehicle.Angle == 45)
                        {
                            launchCell.x -= angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                    }
                    break;
                case WeaponLocation.Stern:
                    if (this.CompVehicle.Angle == 0)
                    {
                        if (this.Pawn.Rotation == Rot4.North)
                        {
                            launchCell.x -= projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.East)
                        {
                            launchCell.x += offset;
                            launchCell.z += projectileOffset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.South)
                        {
                            launchCell.x += projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.West)
                        {
                            launchCell.x += offset;
                            launchCell.z -= projectileOffset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z -= (int)this.Range;
                        }
                    }
                    else
                    {
                        if (this.Pawn.Rotation == Rot4.East && this.CompVehicle.Angle == -45)
                        {
                            launchCell.x += angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.East && this.CompVehicle.Angle == 45)
                        {
                            launchCell.x += angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompVehicle.Angle == -45)
                        {
                            launchCell.x -= angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompVehicle.Angle == 45)
                        {
                            launchCell.x -= angleOffset.First;
                            launchCell.z += angleOffset.Second;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)(Math.Cos(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompVehicle.Angle.DegreesToRadians()) * this.Range);
                        }
                    }
                    break;
            }
            LocalTargetInfo target = new LocalTargetInfo(targetCell);
            ShootLine shootLine;
            bool flag = TryFindShootLineFromTo(this.Pawn.Position, target, out shootLine);

            //FIX FOR MULTIPLAYER
            IntVec3 c = target.Cell + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(cannon.cannonDef.spreadRadius * (this.Range / cannon.cannonDef.maxRange)))];
            Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, this.Pawn.Position, this.Pawn.Map, WipeMode.Vanish);
            if (cannon.cannonDef.cannonSound is null) SoundDefOf_Ships.Explosion_PirateCannon.PlayOneShot(new TargetInfo(this.Pawn.Position, this.Pawn.Map, false));
            else { cannon.cannonDef.cannonSound.PlayOneShot(new TargetInfo(this.Pawn.Position, this.Pawn.Map, false)); }
            if(cannon.cannonDef.moteCannon != null)
                MoteMaker.MakeStaticMote(launchCell, this.Pawn.Map, cannon.cannonDef.moteCannon, 1f);
            projectile2.Launch(this.Pawn, launchCell, c, target, cannon.cannonDef.hitFlags);
        }

        private SPTuple2<float, float> AngleRotationProjectileOffset(float preOffsetX, float preOffsetY)
        {
            SPTuple2<float, float> offset = new SPTuple2<float, float>(preOffsetX, preOffsetY);
            switch (this.Pawn.Rotation.AsInt)
            {
                case 1:
                    if (this.CompVehicle.Angle == -45)
                    {
                        SPTuple2<float, float> newOffset = SPTrig.RotatePointCounterClockwise(preOffsetX, preOffsetY, 45f);
                        offset.First = newOffset.First;
                        offset.Second = newOffset.Second;
                    }
                    else if (this.CompVehicle.Angle == 45)
                    {
                        SPTuple2<float, float> newOffset = SPTrig.RotatePointClockwise(preOffsetX, preOffsetY, 45f);
                        offset.First = newOffset.First;
                        offset.Second = newOffset.Second;
                    }
                    break;
                case 3:
                    if (this.CompVehicle.Angle == -45)
                    {
                        SPTuple2<float, float> newOffset = SPTrig.RotatePointClockwise(preOffsetX, preOffsetY, 225f);
                        offset.First = newOffset.First;
                        offset.Second = newOffset.Second;
                    }
                    else if (this.CompVehicle.Angle == 45)
                    {
                        SPTuple2<float, float> newOffset = SPTrig.RotatePointCounterClockwise(preOffsetX, preOffsetY, 225f);
                        offset.First = newOffset.First;
                        offset.Second = newOffset.Second;
                    }
                    break;
            }
            return offset;
        }

        public bool TryFindShootLineFromTo(IntVec3 root, LocalTargetInfo targ, out ShootLine resultingLine)
        {
            resultingLine = new ShootLine(root, targ.Cell);
            return false;
        }

        public override void CompTick()
        {
            base.CompTick();
            ResolveCannons();
            foreach(CannonHandler cannon in Cannons)
            {
                cannon.DoTick();
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if(!respawningAfterLoad)
            {
                InitializeCannons();
            }

            foreach(CannonHandler cannon in Cannons)
            {
                if(!string.IsNullOrEmpty(cannon.key))
                {
                    Cannons.Where(x => x.parentKey == cannon.key).ToList().ForEach(y => y.attachedTo = cannon);
                }
            }

            broadsideFire = new List<SPTuple<Stack<int>, CannonHandler, int>>();
            multiFireCannon = new List<SPTuple<int, CannonHandler, SPTuple2<int,int>>>();
        }

        private void InitializeCannons()
        {
            if(Cannons.Count <= 0 && Props.cannons.AnyNullified())
            {
                foreach(CannonHandler cannon in Props.cannons)
                {
                    var cannonPermanent = new CannonHandler(this.Pawn, cannon);
                    cannonPermanent.SetTarget(LocalTargetInfo.Invalid);
                    cannonPermanent.ResetCannonAngle();
                    Cannons.Add(cannonPermanent);
                }

                if(Cannons.Select(x => x.key).GroupBy(y => y).AnyNullified(key => key.Count() > 1))
                {
                    Log.Warning("Duplicate CannonHandler key has been found. These are intended to be unique.");
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref cannons, "cannons", LookMode.Deep);
            Scribe_Values.Look(ref range, "range", MaxRangeGrouped);
        }
    }
}
