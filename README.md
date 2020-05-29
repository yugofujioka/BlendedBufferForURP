# fShrinkBuffer4URP
Shrink Buffer for Universal RP

This ScriptableRendererFeature is useful to render VFX that has high filrate.

![image](https://user-images.githubusercontent.com/24952685/83267124-b9d91c00-a1fe-11ea-8c6b-d6547b2b92a4.png)
![image](https://user-images.githubusercontent.com/24952685/83267198-d37a6380-a1fe-11ea-8363-eac063f8daf9.png)


## How to use
- Add ScriptableRendererFeature(fShrinkBufferFeature) to FowardRendererData.
- Disable LayerMask you want to use on ShrinkBuffer from Filtering
- Configure settings for ShrinkBuffer

![image](https://user-images.githubusercontent.com/24952685/83267740-9e224580-a1ff-11ea-9f56-2a6514d382c7.png)

## Required
Unity 2019.3.14f1 or later
URP v7.3.1 or later
