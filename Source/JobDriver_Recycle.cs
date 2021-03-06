﻿using System;

using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mending
{
    public class JobDriver_Recycle : JobDriver_DoBill
    {
        const int fixedHitPointsPerCycle = 5;
        const int fixedFailedDamage = 50;

        int processedHitPoints;
        float workCycle;
        float workCycleProgress;
        ChanceDef failChance;

        protected override Toil DoBill ()
        {
            var objectThing = job.GetTarget (objectTI).Thing;
            var qualityComponent = objectThing.TryGetComp<CompQuality> ();
            var tableThing = job.GetTarget (tableTI).Thing as Building_WorkTable;
            var tablePowerTraderComp = tableThing.GetComp<CompPowerTrader> ();

            var toil = new Toil ();
            toil.initAction = delegate {
                job.bill.Notify_DoBillStarted (pawn);

                processedHitPoints = 0;
                failChance = ChanceDef.GetFor (objectThing);

                workCycleProgress = workCycle = Math.Max (job.bill.recipe.workAmount, 10f);
            };
            toil.tickAction = delegate {
                if (objectThing == null || objectThing.Destroyed) {
                    pawn.jobs.EndCurrentJob (JobCondition.Incompletable);
                }

                workCycleProgress -= StatExtension.GetStatValue (pawn, StatDefOf.WorkToMake, true);

                tableThing.UsedThisTick ();
                if (!tableThing.UsableNow) {
                    pawn.jobs.EndCurrentJob (JobCondition.Incompletable);
                }

                if (workCycleProgress <= 0) {
                    objectThing.HitPoints -= fixedHitPointsPerCycle;

                    if (tablePowerTraderComp != null && tablePowerTraderComp.PowerOn) {
                        processedHitPoints += fixedHitPointsPerCycle;
                    } else {
                        processedHitPoints += fixedHitPointsPerCycle / 2;
                    }

                    float skillPerc = 0.5f;

                    var skillDef = job.RecipeDef.workSkill;
                    if (skillDef != null) {
                        var skill = pawn.skills.GetSkill (skillDef);

                        if (skill != null) {
                            skillPerc = (float)skill.Level / 20f;

                            skill.Learn (0.11f * job.RecipeDef.workSkillLearnFactor);
                        }
                    }

                    if (qualityComponent != null && qualityComponent.Quality > QualityCategory.Awful) {
                        var qc = qualityComponent.Quality;

                        float skillFactor = Mathf.Lerp (0.5f, 1.5f, skillPerc);

                        if (failChance != null && Rand.Value < failChance.Chance (qc) * skillFactor) {
                            objectThing.HitPoints -= fixedFailedDamage;

                            MoteMaker.ThrowText (pawn.DrawPos, pawn.Map, "Failed");
                        }
                    }

                    pawn.GainComfortFromCellIfPossible ();

                    if (objectThing.HitPoints <= 0) {
                        pawn.Map.reservationManager.Release (job.targetB, pawn, job);
                        objectThing.Destroy (DestroyMode.Vanish);

                        float skillFactor = Mathf.Lerp (0.5f, 1.5f, skillPerc);
                        float healthPerc = (float)processedHitPoints / (float)objectThing.MaxHitPoints;
                        float healthFactor = Mathf.Lerp (0f, 0.4f, healthPerc);

                        var list = JobDriverUtils.Reclaim (objectThing, skillFactor * healthFactor);

                        if (list.Count > 1) {
                            for (int j = 1; j < list.Count; j++) {
                                if (!GenPlace.TryPlaceThing (list [j], pawn.Position, pawn.Map, ThingPlaceMode.Near, null)) {
                                    Log.Error ("Mending :: " + pawn + " could not drop recipe product " + list [j] + " near " + pawn.Position);
                                }
                            }
                        } else if (list.Count == 1) {
                            list [0].SetPositionDirect (pawn.Position);

                            job.bill.Notify_IterationCompleted (pawn, list);
                            job.targetB = list [0];

                            pawn.Map.reservationManager.Reserve (pawn, job, job.targetB, 1);
                        } else {
                            Log.Message ("Mending :: " + pawn + " could not reclaim anything from " + objectThing);
                        }

                        ReadyForNextToil ();
                    }

                    workCycleProgress = workCycle;
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.WithEffect (() => job.bill.recipe.effectWorking, tableTI);
            toil.PlaySustainerOrSound (() => toil.actor.CurJob.bill.recipe.soundWorking);
            toil.WithProgressBar (tableTI, delegate {
                return (float)objectThing.HitPoints / (float)objectThing.MaxHitPoints;
            }, false, 0.5f);
            toil.FailOn (() => {
                return toil.actor.CurJob.bill.suspended || !tableThing.UsableNow;
            });
            return toil;
        }

    }
}

