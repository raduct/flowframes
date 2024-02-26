# Flowframes - Windows GUI for Video Interpolation


This is a branch of n00mkrad flowframes.
https://github.com/n00mkrad/flowframes

It contains several changes and optimizations, mainly to support interpolation of 3D movies in frame-packed format. Interpolation is performed in parallel for each eye and then frames are recombined in Full SBS format.
You may use BD3D2MK3D to extract streams from 3D BD and custom mux after interpolation with flowframes.
Only RIFE Vulkan NCNN interpolation is supported as this performed best for me. I recommend version 4.14 for best quality or 4.6 for better speed. Use the video encoder of your choice, I use software AV1 high (CR 26), slow (preset 6), 10bit.

Only the source code is provided as is, without guarantee or support.
 

