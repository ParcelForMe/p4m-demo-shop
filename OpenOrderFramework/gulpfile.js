/// <binding BeforeBuild='copyBowerComponents' />
/*
This file in the main entry point for defining Gulp tasks and using Gulp plugins.
Click here to learn more. http://go.microsoft.com/fwlink/?LinkId=518007
*/

var gulp = require('gulp');
var del = require('del');
var gutil = require("gulp-util");

gulp.task('copyBowerComponents', function () {
    // We clear the Scripts/Lib to ensure that the Scripts/Lib directory does not 
    // include any files that have since been removed from referenced Bower packages
    gutil.log("Cleaning ./Scripts/Lib/**/*");
    var result = del([
        './Scripts/Lib/**/*'
    ]);

    if (result) {
        gutil.log("Copying ./bower_components/**/* to ./Scripts/Lib");
        return gulp.src("./bower_components/**/*").pipe(gulp.dest("./Scripts/Lib"));
    } else {
        gutil.log(gutil.colors.red("Failed to cleaning ./Scripts/Lib/**/*"));
        return result;
    }
});