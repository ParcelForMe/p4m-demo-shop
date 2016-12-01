/// <binding BeforeBuild='copyBowerComponents' />
/*
This file in the main entry point for defining Gulp tasks and using Gulp plugins.
Click here to learn more. http://go.microsoft.com/fwlink/?LinkId=518007
*/

var gulp = require('gulp');

gulp.task('copyBowerComponents', function () {
    gulp.src("./bower_components/**/*").pipe(gulp.dest("./Scripts/Lib"));
});