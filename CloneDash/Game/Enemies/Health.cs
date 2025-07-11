﻿using CloneDash.Scenes;

using Nucleus.Engine;

namespace CloneDash.Game.Entities
{
	public class Health : DashBonusEntity
	{
		public Health() : base(EntityType.Heart) {
			Interactivity = EntityInteractivity.SamePath;
			DeathAddsToCombo = false;
			DoesDamagePlayer = true;
		}

		protected override void OnHit(PathwaySide side, double distanceToHit) {
			// Hack to trick Kill into not calling RewardPlayer without heal.
			// Set it back right after
			DoesRewardPlayer = false;
			Kill();
			DoesRewardPlayer = true;
			RewardPlayer(true);
		}

		public override void Build() {
			base.Build();
			var level = Level.As<DashGameLevel>();
			var scene = level.Scene;
			Model = scene.GetEnemyModel(this).Instantiate();
			ApproachAnimation = Model.Data.FindAnimation(scene.GetEnemyApproachAnimation(this, out _));
			OutAnimation = Model.Data.FindAnimation(scene.GetEnemyHitAnimation(this, HitAnimationType.Perfect));
		}
	}
}