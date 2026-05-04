using HardcoreWater;
using HardcoreWater.ModBlock;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace HardcoreWater.ModBlockEntity
{
    public class BlockEntityAqueductSluice : BlockEntityAqueduct
	{
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

        public bool OnInteract(BlockSelection blockSel, IPlayer byPlayer)
        {
            isOpen = !isOpen;
            // Play sound and animation here
            MarkDirty(true);
            Console.WriteLine($"Sluice opened: {isOpen}");
            return true;
        }


        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
		{
			base.FromTreeAttributes(tree, worldAccessForResolve);
            this.isOpen = tree.GetBool("IsOpen", false);

        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("IsOpen", this.isOpen);
        }
    }
}