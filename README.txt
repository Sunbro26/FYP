Adding ML Agents to project

ML Agents requires python version 3.10.12 to work. Download the source from here.
https://www.python.org/downloads/release/python-31012/
As no installer exists for python, it needs to be compiled manually.
For Windows,
Make sure Visual Studio Build Tools are installed from this directory
https://visualstudio.microsoft.com/downloads/
(Only build tools, nothing else)
cd Python-3.10.12
cd PCBuild
build.bat -p x64

cd amd64
python.exe --version 
To check if it is working

Now cd to project directory
cd python-envs
Create a venv
Python3.10.12directory/PCBuild/amd64/python.exe-m venv .venv
cd .venv
pip install mlagents

Should be set up now. 
