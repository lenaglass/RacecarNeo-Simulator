#!/usr/bin/env bash

# NOTE: This is an abbreviated version of the full setup that uses anaconda
# on MacOS and supporting only the simulator environment (not an actual robot).
#
# Controller mappings are for a Playstation 5 Dualshock Controller
# (Haptics are supported when connected via USB, but not via Blutetooth).
#
# Prior to running this script, install:
#
#  - Anaconda (https://www.anaconda.com/download)
#  - Visual Stuido Code (*do this first so Unity finds it during install)
#    - Package Manager -> Install "Unity (by Microsoft)"
#  - Unity Hub
#    - During install, select integration with VS Code
#    - "Open Project from File..." (upper right on Projects tab)
#    - Install the recommended Unity version for the project
#    - Open the "RacecarNeo-Simulator" directory as the project root

CONDA_ENV="racecar"

RC_NEO_LIB="https://github.com/MITRacecarNeo/racecar-neo-library.git"
RC_NEO_LABS="https://github.com/MITRacecarNeo/racecar-neo-"

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
RACECAR_DIR=$(dirname "${SCRIPT_DIR}")/racecar-neo-installer

echo "Creating conda environment: ${CONDA_ENV}"
conda create -n "${CONDA_ENV}" python=3.10 -y && conda activate  "${CONDA_ENV}"
conda install -y -c conda-forge ffmpeg notebook

echo "Installing dependencies..."
mkdir "${RACECAR_DIR}" && cd "${RACECAR_DIR}" 
pip install -r racecar-student/scripts/requirements.txt
pip install torch torchvision ultralytics scikit-learn matplotlib pillow

echo "Installing labs..."
git clone "${RC_NEO_LIB}" --single-branch --depth 1
mv racecar-neo-library/library library
rm -rf racecar-neo-library

git clone "${RC_NEO_LABS}oneshot-labs.git" --single-branch --depth 1
git clone "${RC_NEO_LABS}neo-outreach-labs.git" --single-branch --depth 1
git clone "${RC_NEO_LABS}rereq-labs.git" --single-branch --depth 1
git clone "${RC_NEO_LABS}mites-labs.git" --single-branch --depth 1

