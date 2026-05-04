using HardcoreWater;
using HardcoreWater.ModBlock;
using System;
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
            MarkDirty(true);
            
        }

        private void CloseSluice()
        {
            
            // animUtil?.StartAnimation(new AnimationMetaData() { Animation = "close", Code = "close"});
            isOpen=false;
            // animUtil.StartAnimation(new AnimationMetaData()
            // {
            //     Animation = "close",
            //     Code = "close"
            // });
            // animUtil.StopAnimation("open");
            TriggerSluiceAnimation();
            // Console.WriteLine("CloseSluice");
            MarkDirty(true);
            
        }

        public bool OnInteract(BlockSelection blockSel, IPlayer byPlayer)
        {
            if (!isOpen)
            {   
                OpenSluice();
                // animUtil?.StartAnimation(new AnimationMetaData() { Animation = "open", Code = "open", EaseInSpeed = 1f, EaseOutSpeed = 100f, AnimationSpeed = 1f });
            
            }
            else if (isOpen)
            {
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
                    Code = "open"
                });
            }
            else
            {
                animUtil?.StopAnimation("open"); // With rewind, stopping animation will rewind to the first, looking like closing
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
		{
			base.FromTreeAttributes(tree, worldAccessForResolve);
            isOpen = tree.GetBool("isOpen", false);


        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("isOpen", isOpen);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (animUtil?.animator == null)
            {
                float rotY = Block.Shape.rotateY;
                animUtil?.InitializeAnimator("aqueductsluice", null, null, new Vec3f(0, rotY, 0));
            }
            return base.OnTesselation(mesher, tessThreadTesselator);
        }
    }
}