KeePass source mirror
=====================

A source code mirror of all [KeePass](https://keepass.info/) v2.x releases.

I created this repository because:

- Source code for KeePass releases is published for each release as a Zip file
- The absence of a public VCS makes viewing changes between releases less easy
- It's simply convenient to operate within a VCS when working with source code

All code resides in the `mirror` branch with each commit a KeePass release.

Please note that ***all*** content is Dominik Reichl's work and not my own.

Assembly notes
--------------

The repository was assembled as follows starting from an empty (orphan) `mirror` branch:

- The source code for the *v2.00* release was retrieved from the [official download site](https://sourceforge.net/projects/keepass/files/KeePass%202.x/)
- The contents of the archive was extracted into the top-level directory of the working tree
- A PowerShell function was run to add empty `.keepme` files to each empty directory (see below)
- All changes in the working directory were committed and an associated release tag created
- The entire working directory was cleared and the process repeated for the next release

### Addition of empty .keepme files

Empty `.keepme` files were added to directories with no children (either files or subdirectories) to ensure they are preserved in the associated commit for the KeePass release. Each KeePass source code archive generally has several empty directories, however, Git only tracks files. Without adding a file to these empty directories they will not be present in the commit. Adding empty placeholder files was from my perspective the least intrusive option to ensure these directories are retained.

### Verifying source code is authentic

This being security sensitive code you may (actually, ***should***!) want to verify that the source code in this repository for a given release has not been tampered with. The simplest way to do so right now is:

1. Download the source code archive for the given KeePass release from the [official download site](https://sourceforge.net/projects/keepass/files/KeePass%202.x/)
1. Checkout the commit for the KeePass release of interest (in a separate branch or detached `HEAD`)
1. Delete **all** files in the working tree (i.e. everything except the `.git` directory)
1. Extract the contents of the archive into the empty working tree
1. Run `git status` from within the Git repository

The only changes should be the addition of the empty `.keepme` placeholder files referenced earlier.

### Final notes

Consider [donating to KeePass](https://keepass.info/donate.html) to thank the author for his time developing the software.
