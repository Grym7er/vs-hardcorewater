using HardcoreWater;
using HardcoreWater.ModBlock;
using System;
using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Client;
using System.Runtime.CompilerServices;


namespace HardcoreWater.ModBlockEntity
{
    public class BlockEntityAqueductSluice : BlockEntityAqueduct
	{
        BlockEntityAnimationUtil animUtil
        {
            get { return GetBehavior<BEBehaviorAnimatable>().animUtil; }
        }
        private bool isOpen = false;

        public bool IsOpen
        {
            get => this.isOpen;
            set => this.isOpen = value;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

        }

        // ToDo: Create a method that replace's the fluid in the sluice block with air, if:
        // The sluice goes from open to closed.
        // To water source is on the closed end.

        private void ConditionallyReplaceFluidWithAir()
        {
            BlockFacing closedFace = BlockFacing.FromFirstLetter((this.Block as IAqueduct).Orientation[0]);
            // Get the direction that the water is coming from
            BlockFacing watercomefromfacing = BlockFacing.HORIZONTALS.FirstOrDefault(f => this.Pos.AddCopy(f).Equals(this.WaterSourcePos));
            bool shouldReplaceFluid = watercomefromfacing == closedFace;
            if (shouldReplaceFluid)
            {
                this.Api.World.BlockAccessor.SetBlock(0, this.Pos, BlockLayersAccess.Fluid);
                this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos);
            }
        }

        private void OpenSluice()
        {
            
            // animUtil?.StartAnimation(new AnimationMetaData() { Animation = "open", Code = "open"});
            isOpen=true;
            // animUtil.StartAnimation(new AnimationMetaData()
            // {
            //     Animation = "open",
            //     Code = "open"
            // });
            // animUtil.StopAnimation("close");
            // Console.WriteLine("OpenSluice");
            TriggerSluiceAnimation();
            this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos); // Hopefully fixes the 'not flowing into air' issue
            MarkDirty(true);
            
        }

        private void CloseSluice()
        {
            
            // animUtil?.StartAnimation(new AnimationMetaData() { Animation = "close", Code = "close"});
            isOpen=false;
            // ConditionallyReplaceFluidWithAir();
            // animUtil.StartAnimation(new AnimationMetaData()
            // {
            //     Animation = "close",
            //     Code = "close"
            // });
            // animUtil.StopAnimation("open");
            TriggerSluiceAnimation();
            this.Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(this.Pos); // Hopefully fixes the 'not flowing into air' issue
            MarkDirty(true);
            
            
        }

        public bool OnInteract(BlockSelection blockSel, IPlayer byPlayer)
        {
            if (!isOpen)

            {   Api.World.PlaySoundAt(new AssetLocation("hardcorewaterforked:sounds/effect/sluiceopen.ogg"), Pos, 0, byPlayer);
                OpenSluice();
                
                // animUtil?.StartAnimation(new AnimationMetaData() { Animation = "open", Code = "open", EaseInSpeed = 1f, EaseOutSpeed = 100f, AnimationSpeed = 1f });
            
            }
            else if (isOpen)
            {
                Api.World.PlaySoundAt(new AssetLocation("hardcorewaterforked:sounds/effect/sluiceclose.ogg"), Pos, 0, byPlayer);
                CloseSluice();
               
                // animUtil?.StartAnimation(new AnimationMetaData() { Animation = "close", Code = "close", EaseInSpeed = 1f, EaseOutSpeed = 100f, AnimationSpeed = 1f });
            }

            return true;
        }

        private void TriggerSluiceAnimation()
        {
            // if (Api?.Side == EnumAppSide.Server) return; // client side animations
            
            if (isOpen)
            {
                
                animUtil?.StartAnimation(new AnimationMetaData()
                {
                    Animation = "open",
                    Code = "open",
                    EaseInSpeed = 3f,
                    EaseOutSpeed = 3f,
                    AnimationSpeed = 1f
                });
                
            }
            else
            {
                // TODO - let the rewind take place a bit slower
                animUtil?.StopAnimation("open"); // With rewind, stopping animation will rewind to the first, looking like closing
            }
        }


        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
		{
			base.FromTreeAttributes(tree, worldAccessForResolve);
            isOpen = tree.GetBool("isOpen", false);


            if (worldAccessForResolve.Side == EnumAppSide.Client && Api != null)
            {
                // Forces a tesselation 
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
                // MarkDirty(true);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("isOpen", isOpen);
        }

        private void UpdateSluiceAnimationState()
        {
            bool isOpenAnimationActive = animUtil.activeAnimationsByAnimCode.ContainsKey("open") || animUtil.animator.GetAnimationState("open")?.Active == true;
            // if currentAnimation is not null, and isOpen is true, set the animation to open
            if (isOpen && !isOpenAnimationActive)
            {
                // play the open animation super fast
                animUtil?.StartAnimation(new AnimationMetaData()
                {
                    Animation = "open",
                    Code = "open",
                    EaseInSpeed = 3f,
                    EaseOutSpeed = 3f,
                    AnimationSpeed = 1f
                });
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (animUtil?.animator == null)
            {
                float rotY = Block.Shape.rotateY;
                animUtil?.InitializeAnimator("aqueductsluice", null, null, new Vec3f(0, rotY, 0));
            }

            if (Api.Side == EnumAppSide.Client && animUtil?.animator != null)
            {
                // Set the sluice animation to open once the animUtil is initialized
                UpdateSluiceAnimationState();
            }

            return base.OnTesselation(mesher, tessThreadTesselator);
        }
    }
}