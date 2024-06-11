#!/bin/bash

files=$(grep -o '<Compile Include="[^"]*"' SoundFromAssetBundle.csproj | sed 's/<Compile Include="//; s/"//')
echo "$files" > SoundFromAssetBundle.cslist
